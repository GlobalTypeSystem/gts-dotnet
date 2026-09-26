using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Application;

/// <summary>Adds entities to a registry with the same rules as the GTS HTTP API.</summary>
public static class GtsEntityOperations
{
    public sealed record AddResult(bool Ok, string Id, string? SchemaId, bool IsSchema, string? Error, bool Conflict = false);

    public static async Task<AddResult> TryAddAsync(
        GtsRegistry registry,
        JsonObject body,
        bool validate,
        GtsExtractOptions? extractOptions = null,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opt = extractOptions ?? GtsExtractOptions.Default;
        var entity = GtsJsonEntity.ExtractEntity(body, opt);
        var extract = GtsJsonEntity.ExtractId(body, opt);

        if (!entity.IsSchema)
        {
            if (string.IsNullOrEmpty(entity.SelectedEntityField))
                return new AddResult(false, "", null, false, "Unable to detect GTS ID in instance entity");
            if (entity.GtsId is { IsInstance: true } instanceId && instanceId.Segments.Count == 1)
                return new AddResult(false, instanceId.Id, null, false, "Single-segment instance IDs are not allowed");
        }
        else
        {
            if (entity.GtsId is null)
                return new AddResult(false, "", null, true, "Unable to detect GTS ID in schema");
        }

        if (entity.IsSchema)
        {
            var rawId = body.TryGetPropertyValue("$id", out var idn) && idn is JsonValue idValue && idValue.TryGetValue<string>(out var id)
                ? id
                : null;
            if (validate && !string.IsNullOrEmpty(rawId) && rawId.StartsWith(GtsConstants.IdPrefix, StringComparison.Ordinal) &&
                !rawId.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
                return new AddResult(false, "", null, true, "Schema $id must use gts:// URI format, not plain gts. prefix");
            if (!GtsTypeSchema.TryGetSupportedDialect(body, out _, out var dialectError))
                return new AddResult(false, entity.GtsId?.Id ?? "", null, true, dialectError);
            var keywordErrors = GtsSchemaKeywordValidator.Validate(body);
            if (keywordErrors.Count > 0)
                return new AddResult(false, entity.GtsId?.Id ?? "", null, true, string.Join("; ", keywordErrors));
            var refErrors = GtsRefValidator.ValidatePatterns(body, entity.GtsId?.Id ?? "");
            if (refErrors.Count > 0)
                return new AddResult(false, entity.GtsId?.Id ?? "", null, true, "x-gts-ref validation failed: " + string.Join("; ", refErrors));
        }

        try
        {
            GtsSchemaRefFormatValidator.ValidateRefs(body);
        }
        catch (Exception ex)
        {
            return new AddResult(false, "", null, entity.IsSchema, ex.Message);
        }

        GtsJsonEntity? existing = entity.GtsId is not null
            ? await registry.GetAsync(entity.GtsId).ConfigureAwait(false)
            : !string.IsNullOrEmpty(extract.Id)
                ? await registry.GetByInstanceIdAsync(extract.Id).ConfigureAwait(false)
                : null;
        if (existing is not null)
        {
            if (JsonNode.DeepEquals(existing.Content, entity.Content) && !validate)
                return new AddResult(true, extract.Id ?? entity.GtsId?.Id ?? "", entity.SchemaId, entity.IsSchema, null);
            if (!JsonNode.DeepEquals(existing.Content, entity.Content))
                return new AddResult(false, extract.Id ?? entity.GtsId?.Id ?? "", entity.SchemaId, entity.IsSchema,
                    "Entity already exists with different content", true);
        }

        if (entity.IsSchema && entity.GtsId is not null)
        {
            if (validate)
            {
                var registered = await registry.SnapshotForReadAsync().ConfigureAwait(false);
                var constraintErrors = GtsRefValidator.ValidateConstraints(entity.Content, entity.GtsId.Id, registered, refValidationMode);
                if (constraintErrors.Count > 0)
                    return new AddResult(false, entity.GtsId.Id, entity.SchemaId, true, "x-gts-ref validation failed: " + string.Join("; ", constraintErrors));
                var schemaVr = await registry.ValidateSchemaAsync(entity.GtsId, entity.Content, cancellationToken, refValidationMode)
                    .ConfigureAwait(false);
                if (!schemaVr.Ok)
                {
                    var msg = schemaVr.Errors is { Count: > 0 }
                        ? string.Join("; ", schemaVr.Errors)
                        : (schemaVr.FailureReason?.ToWire() ?? "Schema validation failed");
                    return new AddResult(false, entity.GtsId.Id, entity.SchemaId, true, msg);
                }
            }

            if (await registry.TrySaveAsync(entity).ConfigureAwait(false) == GtsSaveOutcome.Conflict)
                return new AddResult(false, entity.GtsId.Id, entity.SchemaId, true, "Entity already exists with different content", true);

            var schemaIdOut = entity.GtsId.Id;
            return new AddResult(true, schemaIdOut, string.IsNullOrEmpty(entity.SchemaId) ? null : entity.SchemaId, true, null);
        }

        if (validate && !entity.IsSchema)
        {
            if (string.IsNullOrEmpty(entity.SchemaId) || !GtsId.TryParse(entity.SchemaId, out var schemaId) || schemaId is null || !schemaId.IsType)
                return new AddResult(false, entity.GtsId?.Id ?? extract.Id ?? "", entity.SchemaId, false, "Unable to determine instance type");
            var validation = await registry.ValidateJsonAsync(entity.Content, schemaId, entity.GtsId?.Id ?? extract.Id, cancellationToken, refValidationMode)
                .ConfigureAwait(false);
            if (!validation.Ok)
            {
                var error = validation.SchemaErrors is { Count: > 0 }
                    ? string.Join("; ", validation.SchemaErrors)
                    : validation.FailureReason?.ToWire() ?? "Validation failed";
                return new AddResult(false, entity.GtsId?.Id ?? extract.Id ?? "", entity.SchemaId, false, error);
            }
        }

        var idOut = entity.GtsId?.Id ?? extract.Id ?? "";
        if (await registry.TrySaveAsync(entity).ConfigureAwait(false) == GtsSaveOutcome.Conflict)
            return new AddResult(false, idOut, entity.SchemaId, entity.IsSchema, "Entity already exists with different content", true);
        return new AddResult(true, idOut, string.IsNullOrEmpty(entity.SchemaId) ? null : entity.SchemaId, entity.IsSchema, null);
    }
}
