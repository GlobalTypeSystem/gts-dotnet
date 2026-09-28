using System.Text.Json;
using System.Text.Json.Nodes;
using Gts;
using Gts.Store;

namespace Gts.Application;

internal static class GtsOperationEndpoints
{
    internal static void Map(WebApplication app, GtsRegistry registry)
    {
        app.MapGet("/compatibility", (string old_type_id, string new_type_id) => Compatibility(registry, old_type_id, new_type_id));
        app.MapPost("/cast", (HttpRequest request) => Cast(request, registry));
        app.MapGet("/query", async (string expr, int? limit) =>
        {
            var normalizedLimit = limit is >= 1 and <= 1000 ? limit.Value : 100;
            var result = await GtsQueryEngine.ExecuteAsync(registry, expr, normalizedLimit).ConfigureAwait(false);
            return result.Error is null
                ? Results.Json(new { results = result.Results, count = result.Results.Count, limit = normalizedLimit })
                : Results.Json(new { error = result.Error, results = Array.Empty<object>(), limit = normalizedLimit });
        });
        app.MapGet("/attr", (string gts_with_path) => Attribute(registry, gts_with_path));
    }

    private static async Task<IResult> Compatibility(GtsRegistry registry, string oldTypeId, string newTypeId)
    {
        if (!GtsId.TryParse(oldTypeId, out var oldId) || oldId is null || !GtsId.TryParse(newTypeId, out var newId) || newId is null)
            return Results.Json(new { old = oldTypeId, @new = newTypeId, is_backward_compatible = false, is_forward_compatible = false, is_fully_compatible = false, backward_errors = new[] { "Invalid id" }, forward_errors = new[] { "Invalid id" } });
        var oldEntity = await registry.GetAsync(oldId).ConfigureAwait(false);
        var newEntity = await registry.GetAsync(newId).ConfigureAwait(false);
        if (oldEntity is null || newEntity is null || !oldEntity.IsSchema || !newEntity.IsSchema)
            return Results.Json(new { old = oldTypeId, @new = newTypeId, is_backward_compatible = false, is_forward_compatible = false, is_fully_compatible = false, backward_errors = new[] { "Schema not found" }, forward_errors = new[] { "Schema not found" } });

        var oldDialect = GtsHttpApiExtensions.NormalizeDialect(oldEntity.Content["$schema"]?.GetValue<string>());
        var newDialect = GtsHttpApiExtensions.NormalizeDialect(newEntity.Content["$schema"]?.GetValue<string>());
        if (oldDialect != newDialect || GtsHttpApiExtensions.ContainsSchemaKeyword(oldEntity.Content, "if") || GtsHttpApiExtensions.ContainsSchemaKeyword(newEntity.Content, "if"))
            return Results.Json(new { old = oldTypeId, @new = newTypeId, backward_compatibility = "unknown", forward_compatibility = "unknown", full_compatibility = "unknown" });

        var registered = await registry.GetAllAsync().ConfigureAwait(false);
        var schemas = registered.Where(entity => entity.IsSchema && entity.GtsId is not null)
            .ToDictionary(entity => entity.GtsId!.Id, entity => entity.Content, StringComparer.Ordinal);
        var oldFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(GtsHttpApiExtensions.ResolveSchemaRefs(oldEntity.Content, schemas, new HashSet<string>()));
        var newFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(GtsHttpApiExtensions.ResolveSchemaRefs(newEntity.Content, schemas, new HashSet<string>()));
        var (backward, backwardErrors, forward, forwardErrors) = GtsSchemaCompatibilityService.CompareEvolution(oldFlat, newFlat);
        return Results.Json(new
        {
            old = oldTypeId,
            @new = newTypeId,
            backward_compatibility = backward ? "compatible" : "incompatible",
            forward_compatibility = forward ? "compatible" : "incompatible",
            full_compatibility = backward && forward ? "compatible" : "incompatible",
            is_backward_compatible = backward,
            is_forward_compatible = forward,
            is_fully_compatible = backward && forward,
            backward_errors = backwardErrors,
            forward_errors = forwardErrors
        });
    }

    private static async Task<IResult> Cast(HttpRequest request, GtsRegistry registry)
    {
        var body = await JsonSerializer.DeserializeAsync<CastRequest>(request.Body, GtsHttpJson.Options).ConfigureAwait(false);
        if (string.IsNullOrEmpty(body?.InstanceId))
            return Results.Json(new { error = "instance_id required" });
        var instanceId = body.InstanceId;
        var targetId = body.ToTypeId ?? body.ToSchemaId;
        if (string.IsNullOrEmpty(targetId))
            return Results.Json(new { error = "to_type_id required" });
        if (!GtsId.TryParse(targetId, out var targetGtsId) || targetGtsId is null || !targetGtsId.IsType)
            return Results.Json(new { error = "Invalid target schema id" });

        var source = await registry.GetByInstanceIdAsync(instanceId).ConfigureAwait(false);
        var target = await registry.GetAsync(targetGtsId).ConfigureAwait(false);
        if (source is not null && target is not null && GtsId.TryParse(source.SchemaId, out var sourceType) && sourceType is not null)
        {
            var sourceSchema = await registry.GetAsync(sourceType).ConfigureAwait(false);
            if (sourceSchema is not null && GtsHttpApiExtensions.NormalizeDialect(sourceSchema.Content["$schema"]?.GetValue<string>()) != GtsHttpApiExtensions.NormalizeDialect(target.Content["$schema"]?.GetValue<string>()))
                return Results.Json(new { casted_entity = source.Content.DeepClone(), backward_compatibility = "unknown", forward_compatibility = "unknown", full_compatibility = "unknown" });
        }

        var result = await registry.CastInstanceAsync(instanceId, targetGtsId).ConfigureAwait(false);
        if (!result.Ok)
            return Results.Json(new
            {
                error = result.FailureReason == GtsValidationFailure.NotAnInstance ? "Source entity must be an instance" : result.FailureReason?.ToWire(),
                instance_id = result.InstanceId,
                from_schema_id = result.FromSchemaId?.Id,
                to_schema_id = result.ToSchemaId?.Id,
                schema_validation_errors = result.SchemaValidationErrors,
                casted_entity = result.CastedContent?.DeepClone(),
                are_minor_variant_pair = result.Comparison?.AreMinorVariantPair,
                is_structurally_compatible = result.Comparison?.IsStructurallyCompatible,
                is_backward_compatible = result.Comparison?.IsBackwardEvolutionCompatible,
                is_forward_compatible = result.Comparison?.IsForwardEvolutionCompatible
            });

        var fromSchema = result.FromSchemaId is null ? null : await registry.GetAsync(result.FromSchemaId).ConfigureAwait(false);
        var toSchema = result.ToSchemaId is null ? null : await registry.GetAsync(result.ToSchemaId).ConfigureAwait(false);
        var distinctDialects = fromSchema is not null && toSchema is not null && GtsHttpApiExtensions.NormalizeDialect(fromSchema.Content["$schema"]?.GetValue<string>()) != GtsHttpApiExtensions.NormalizeDialect(toSchema.Content["$schema"]?.GetValue<string>());
        var comparison = result.Comparison!;
        var enumChange = fromSchema is not null && toSchema is not null ? GtsHttpApiExtensions.EnumConstraintChange(fromSchema.Content, toSchema.Content) : 0;
        var backward = distinctDialects ? "unknown" : enumChange < 0 ? "compatible" : enumChange > 0 ? "incompatible" : comparison.IsBackwardEvolutionCompatible ? "compatible" : "incompatible";
        var forward = distinctDialects ? "unknown" : enumChange < 0 ? "incompatible" : enumChange > 0 ? "compatible" : comparison.IsForwardEvolutionCompatible ? "compatible" : "incompatible";
        return Results.Json(new
        {
            casted_entity = result.CastedContent!.DeepClone(),
            backward_compatibility = backward,
            forward_compatibility = forward,
            full_compatibility = distinctDialects ? "unknown" : backward == "compatible" && forward == "compatible" ? "compatible" : "incompatible",
            is_backward_compatible = comparison.IsBackwardEvolutionCompatible,
            is_forward_compatible = comparison.IsForwardEvolutionCompatible,
            is_structurally_compatible = comparison.IsStructurallyCompatible
        });
    }

    private static async Task<IResult> Attribute(GtsRegistry registry, string selector)
    {
        var result = await registry.GetAttributeAsync(selector).ConfigureAwait(false);
        if (!result.Resolved)
            return Results.Json(new { resolved = false, error = result.Error, available_fields = result.AvailableFields });
        return result.Value switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => Results.Json(new { resolved = true, value = text }),
            JsonValue value when value.TryGetValue<bool>(out var boolean) => Results.Json(new { resolved = true, value = boolean }),
            JsonValue value when value.TryGetValue<int>(out var integer) => Results.Json(new { resolved = true, value = integer }),
            JsonValue value when value.TryGetValue<double>(out var number) => Results.Json(new { resolved = true, value = number }),
            JsonValue value when value.TryGetValue<decimal>(out var decimalNumber) => Results.Json(new { resolved = true, value = decimalNumber }),
            _ => Results.Json(new { resolved = true, value = result.Value?.DeepClone() })
        };
    }
}