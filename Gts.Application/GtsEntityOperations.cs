using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Application;

/// <summary>Adds entities to a registry with the same rules as the GTS HTTP API.</summary>
public static class GtsEntityOperations
{
    public sealed record AddResult(bool Ok, string Id, string? SchemaId, bool IsSchema, string? Error);

    public static async Task<AddResult> TryAddAsync(
        GtsRegistry registry,
        JsonObject body,
        bool validate,
        GtsExtractOptions? extractOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opt = extractOptions ?? GtsExtractOptions.Default;
        var entity = GtsJsonEntity.ExtractEntity(body, opt);
        var extract = GtsJsonEntity.ExtractId(body, opt);

        if (!entity.IsSchema)
        {
            if (string.IsNullOrEmpty(entity.SelectedEntityField))
                return new AddResult(false, "", null, false, "Instance must have an id field");
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
            if (validate && !string.IsNullOrEmpty(rawId) && rawId.StartsWith("gts.", StringComparison.Ordinal) &&
                !rawId.StartsWith("gts://", StringComparison.Ordinal))
                return new AddResult(false, "", null, true, "Schema $id must use gts:// URI format, not plain gts. prefix");
            if (!TryGetSupportedDialect(body, out var dialectError))
                return new AddResult(false, entity.GtsId?.Id ?? "", null, true, dialectError);
        }

        try
        {
            GtsSchemaRefFormatValidator.ValidateRefs(body);
        }
        catch (Exception ex)
        {
            return new AddResult(false, "", null, entity.IsSchema, ex.Message);
        }

        if (entity.IsSchema && entity.GtsId is not null)
        {
            if (validate)
            {
                var schemaVr = await registry.ValidateSchemaAsync(entity.GtsId, entity.Content, cancellationToken)
                    .ConfigureAwait(false);
                if (!schemaVr.Ok)
                {
                    var msg = schemaVr.Errors is { Count: > 0 }
                        ? string.Join("; ", schemaVr.Errors)
                        : (schemaVr.FailureReason ?? "Schema validation failed");
                    return new AddResult(false, entity.GtsId.Id, entity.SchemaId, true, msg);
                }
            }

            await registry.SaveAsync(entity).ConfigureAwait(false);

            var schemaIdOut = entity.GtsId.Id;
            return new AddResult(true, schemaIdOut, string.IsNullOrEmpty(entity.SchemaId) ? null : entity.SchemaId, true, null);
        }

        await registry.SaveAsync(entity).ConfigureAwait(false);

        if (validate && !entity.IsSchema && entity.GtsId is not null)
        {
            var vr = await registry.ValidateInstanceAsync(entity.GtsId.Id, cancellationToken).ConfigureAwait(false);
            if (!vr.Ok)
                return new AddResult(false, entity.GtsId.Id, entity.SchemaId, false, vr.FailureReason ?? "Validation failed");
        }

        var idOut = entity.GtsId?.Id ?? extract.Id ?? "";
        return new AddResult(true, idOut, string.IsNullOrEmpty(entity.SchemaId) ? null : entity.SchemaId, entity.IsSchema, null);
    }

    private static bool TryGetSupportedDialect(JsonObject body, out string? error)
    {
        error = null;
        if (!body.TryGetPropertyValue("$schema", out var node) || node is not JsonValue value ||
            !value.TryGetValue<string>(out var dialect) || string.IsNullOrEmpty(dialect))
        {
            error = "Schema must contain a supported $schema dialect";
            return false;
        }

        if (dialect is "http://json-schema.org/draft-07/schema#" or
            "https://json-schema.org/draft-07/schema#" or
            "https://json-schema.org/draft/2019-09/schema" or
            "https://json-schema.org/draft/2019-09/schema#" or
            "https://json-schema.org/draft/2020-12/schema" or
            "https://json-schema.org/draft/2020-12/schema#")
            return true;

        error = $"Unsupported JSON Schema dialect: {dialect}";
        return false;
    }
}
