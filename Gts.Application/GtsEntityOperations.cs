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
        }
        else
        {
            if (entity.GtsId is null)
                return new AddResult(false, "", null, true, "Unable to detect GTS ID in schema");
        }

        if (validate && entity.IsSchema)
        {
            var rawId = body.TryGetPropertyValue("$id", out var idn) ? idn?.GetValue<string>() : null;
            if (!string.IsNullOrEmpty(rawId) && rawId.StartsWith("gts.", StringComparison.Ordinal) &&
                !rawId.StartsWith("gts://", StringComparison.Ordinal))
                return new AddResult(false, "", null, true, "Schema $id must use gts:// URI format, not plain gts. prefix");
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
            var schemaVr = await registry.ValidateSchemaAsync(entity.GtsId, entity.Content, cancellationToken)
                .ConfigureAwait(false);
            if (!schemaVr.Ok)
            {
                var msg = schemaVr.Errors is { Count: > 0 }
                    ? string.Join("; ", schemaVr.Errors)
                    : (schemaVr.FailureReason ?? "Schema validation failed");
                return new AddResult(false, entity.GtsId.Id, entity.SchemaId, true, msg);
            }

            await registry.SaveAsync(entity).ConfigureAwait(false);

            var idOut = entity.GtsId.Id;
            return new AddResult(true, idOut, string.IsNullOrEmpty(entity.SchemaId) ? null : entity.SchemaId, true, null);
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
}
