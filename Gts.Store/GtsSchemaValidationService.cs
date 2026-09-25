using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

internal sealed class GtsSchemaValidationService(IGtsStore store)
{
    internal async ValueTask<GtsSchemaValidationResult> ValidateStoredAsync(string schemaTypeId, CancellationToken cancellationToken, GtsRefValidationMode refValidationMode)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(schemaTypeId)) return Failure(schemaTypeId, GtsValidationFailure.InvalidSchemaId);
        var id = schemaTypeId.Trim();
        if (!GtsId.TryParse(id, out var parsed) || parsed is null || !parsed.IsType) return Failure(id, GtsValidationFailure.InvalidSchemaId);
        var entity = await store.GetAsync(parsed).ConfigureAwait(false);
        if (entity is null) return Failure(id, GtsValidationFailure.SchemaNotFound);
        if (!entity.IsSchema) return Failure(id, GtsValidationFailure.NotASchema);
        return await ValidateAsync(parsed, entity.Content, cancellationToken, refValidationMode).ConfigureAwait(false);
    }

    internal async ValueTask<GtsSchemaValidationResult> ValidateAsync(GtsId schemaId, JsonObject document, CancellationToken cancellationToken, GtsRefValidationMode refValidationMode)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!schemaId.IsType) return Failure(schemaId.Id, GtsValidationFailure.InvalidSchemaId);
        if (!document.ContainsKey("$schema") && document.ContainsKey("$$schema"))
            document = GtsSchemaDocumentNormalizer.CanonicalizeKeywords(document);

        // The dialect floor is checked here too, not only at ingest, so every validation route
        // (stored schema, /validate-type-schema) enforces it — mirroring the Rust reference, where
        // the dialect check runs first inside the shared per-type validation.
        if (!GtsTypeSchema.TryGetSupportedDialect(document, out _, out var dialectError))
            return new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.UnsupportedDialect, Errors = new[] { dialectError! } };

        var keywordErrors = GtsSchemaKeywordValidator.Validate(document);
        if (keywordErrors.Count > 0)
        {
            var jsonSchemaError = keywordErrors.Any(error => error.StartsWith("JSON Schema", StringComparison.Ordinal));
            return new GtsSchemaValidationResult
            {
                Ok = false,
                SchemaId = schemaId.Id,
                FailureReason = jsonSchemaError ? GtsValidationFailure.InvalidJsonSchema : GtsValidationFailure.InvalidGtsKeyword,
                Errors = jsonSchemaError ? keywordErrors.Select(error => "JSON Schema validation failed: " + error).ToArray() : keywordErrors
            };
        }

        try
        {
            GtsSchemaRefFormatValidator.ValidateRefs(document);
        }
        catch (Exception exception)
        {
            return new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.InvalidRefFormat, Errors = new[] { exception.Message } };
        }

        var entities = await store.SnapshotForReadAsync().ConfigureAwait(false);
        var schemaById = new Dictionary<string, GtsJsonEntity>(StringComparer.Ordinal);
        foreach (var candidate in entities)
        {
            if (candidate.IsSchema && candidate.GtsId is not null)
                schemaById[candidate.GtsId.Id] = candidate;
        }

        JsonObject? Load(GtsId id)
        {
            if (!schemaById.TryGetValue(id.Id, out var entity) || !entity.IsSchema)
                return null;
            return !entity.Content.ContainsKey("$schema") && entity.Content.ContainsKey("$$schema")
                ? GtsSchemaDocumentNormalizer.CanonicalizeKeywords(entity.Content)
                : entity.Content;
        }

        var (derivationValid, derivationErrors) = GtsSchemaDerivationValidator.ValidateAgainstRegistry(schemaId, document, Load);
        if (!derivationValid)
            return new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.PrecedentIncompatible, Errors = new[] { "Derived schema is not compatible with base: " + string.Join("; ", derivationErrors) } };

        var referenceErrors = GtsRefValidator.ValidateSchema(document, schemaId.Id, entities, refValidationMode);
        if (referenceErrors.Count > 0)
            return new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.GtsRefValidationFailed, Errors = referenceErrors };

        var ancestorTypeId = GtsSchemaDerivationValidator.GetParentTypeId(schemaId.Id);
        while (ancestorTypeId is not null && GtsId.TryParse(ancestorTypeId, out var ancestorId) && ancestorId is not null && Load(ancestorId) is JsonObject ancestor)
        {
            var ancestorErrors = GtsSchemaTraitsValidator.Validate(ancestorId, ancestor, Load, entities, refValidationMode);
            if (ancestorErrors.Count > 0)
                return new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.TraitValidationFailed, Errors = ancestorErrors.Select(error => $"Ancestor '{ancestorTypeId}' is invalid: {error}").ToArray() };
            ancestorTypeId = GtsSchemaDerivationValidator.GetParentTypeId(ancestorTypeId);
        }

        var traitErrors = GtsSchemaTraitsValidator.Validate(schemaId, document, Load, entities, refValidationMode);
        return traitErrors.Count > 0
            ? new GtsSchemaValidationResult { Ok = false, SchemaId = schemaId.Id, FailureReason = GtsValidationFailure.TraitValidationFailed, Errors = traitErrors }
            : new GtsSchemaValidationResult { Ok = true, SchemaId = schemaId.Id };
    }

    private static GtsSchemaValidationResult Failure(string? id, GtsValidationFailure reason) => new() { Ok = false, SchemaId = id, FailureReason = reason };
}