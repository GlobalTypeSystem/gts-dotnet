using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

internal sealed class GtsInstanceValidationService(
    IGtsStore store,
    Func<GtsId, CancellationToken, GtsRefValidationMode, ValueTask<GtsSchemaValidationResult>> validateSchema)
{
    internal async ValueTask<GtsInstanceValidationResult> ValidateStoredAsync(
        string instanceId,
        CancellationToken cancellationToken,
        GtsRefValidationMode refValidationMode)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(instanceId))
            return Failure(instanceId, null);
        var id = instanceId.Trim();
        var entity = await store.GetByInstanceIdAsync(id).ConfigureAwait(false);
        if (entity is null) return Failure(id, GtsValidationFailure.InstanceNotFound);
        if (entity.IsSchema) return Failure(id, GtsValidationFailure.NotAnInstance);
        var extracted = GtsJsonEntity.ExtractId(entity.Content);
        if (string.IsNullOrEmpty(extracted.SchemaId) || !extracted.SchemaId.EndsWith('~')) return Failure(id, GtsValidationFailure.SchemaIdMissing);
        if (!GtsId.TryParse(extracted.SchemaId, out var schemaId) || schemaId is null || !schemaId.IsType) return Failure(id, GtsValidationFailure.InvalidSchemaId);
        return await ValidateAsync(entity.Content, schemaId, id, cancellationToken, refValidationMode).ConfigureAwait(false);
    }

    internal async ValueTask<GtsInstanceValidationResult> ValidateAsync(
        JsonObject content,
        GtsId schemaId,
        string? instanceId,
        CancellationToken cancellationToken,
        GtsRefValidationMode refValidationMode)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schemaEntity = await store.GetAsync(schemaId).ConfigureAwait(false);
        if (schemaEntity is null) return Failure(instanceId, GtsValidationFailure.SchemaNotFound);
        if (!schemaEntity.IsSchema) return Failure(instanceId, GtsValidationFailure.NotASchema);
        var schemaDocument = !schemaEntity.Content.ContainsKey("$schema") && schemaEntity.Content.ContainsKey("$$schema")
            ? GtsSchemaDocumentNormalizer.CanonicalizeKeywords(schemaEntity.Content)
            : schemaEntity.Content;
        if (schemaDocument[GtsSchemaKeywords.Abstract] is JsonValue abstractValue && abstractValue.TryGetValue<bool>(out var abstractType) && abstractType)
            return Failure(instanceId, GtsValidationFailure.AbstractTypeNotInstantiable);

        var schemaValidation = await validateSchema(schemaId, cancellationToken, refValidationMode).ConfigureAwait(false);
        if (!schemaValidation.Ok)
            return new GtsInstanceValidationResult { Ok = false, Id = instanceId, FailureReason = GtsValidationFailure.TypeSchemaValidationFailed, SchemaErrors = schemaValidation.Errors };

        var entities = await store.SnapshotForReadAsync().ConfigureAwait(false);
        if (HasMixedDialectReferences(schemaDocument, entities)) return Failure(instanceId, GtsValidationFailure.MixedDialectSchemaGraph);

        // Build id -> schema-entity index and the evaluation-normalized schema map in a single pass, so
        // schema resolution during validation is O(1) per lookup instead of an O(N) linear scan per $ref.
        var schemaEntityById = new Dictionary<string, GtsJsonEntity>(StringComparer.Ordinal);
        var schemas = new Dictionary<GtsId, JsonObject>();
        foreach (var entity in entities)
        {
            if (!entity.IsSchema || entity.GtsId is null)
                continue;
            schemaEntityById[entity.GtsId.Id] = entity;
            schemas[entity.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(entity.Content);
        }
        if (!schemas.ContainsKey(schemaId)) return Failure(instanceId, GtsValidationFailure.SchemaNotFound);

        JsonObject? Load(GtsId id)
        {
            if (!schemaEntityById.TryGetValue(id.Id, out var entity))
                return null;
            var loaded = entity.Content;
            return !loaded.ContainsKey("$schema") && loaded.ContainsKey("$$schema")
                ? GtsSchemaDocumentNormalizer.CanonicalizeKeywords(loaded)
                : loaded;
        }
        var effectiveSchema = GtsSchemaDependencyGraph.Resolve(schemaDocument, Load);
        var evaluation = GtsJsonSchemaEvaluator.Evaluate(content, schemaId, schemas);
        var referenceErrors = GtsRefValidator.ValidateInstance(content, effectiveSchema, schemaId.Id, entities, refValidationMode);
        if (evaluation.IsValid && referenceErrors.Count == 0)
            return new GtsInstanceValidationResult { Ok = true, Id = instanceId };
        return new GtsInstanceValidationResult
        {
            Ok = false,
            Id = instanceId,
            FailureReason = referenceErrors.Count > 0 ? GtsValidationFailure.GtsRefValidationFailed : GtsValidationFailure.SchemaValidationFailed,
            SchemaErrors = referenceErrors.Count > 0 ? referenceErrors : GtsJsonSchemaEvaluator.FlattenErrors(evaluation)
        };
    }

    private static GtsInstanceValidationResult Failure(string? id, GtsValidationFailure? reason) => new() { Ok = false, Id = id, FailureReason = reason };

    private static bool HasMixedDialectReferences(JsonObject schema, IEnumerable<GtsJsonEntity> entities)
    {
        var dialect = GtsSchemaDependencyGraph.Dialect(schema);
        var schemas = entities.Where(entity => entity.IsSchema && entity.GtsId is not null)
            .ToDictionary(entity => entity.GtsId!.Id, entity => entity.Content, StringComparer.Ordinal);
        return HasMixedDialectReferences(schema, dialect, schemas, new HashSet<string>());
    }

    private static bool HasMixedDialectReferences(JsonNode? node, string dialect, IReadOnlyDictionary<string, JsonObject> schemas, HashSet<string> visited)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
            {
                var id = GtsConstants.StripUriPrefix(reference).Split('#')[0];
                if (schemas.TryGetValue(id, out var target) && visited.Add(id) && GtsSchemaDependencyGraph.Dialect(target) != dialect)
                    return true;
            }
            return obj.Any(property => HasMixedDialectReferences(property.Value, dialect, schemas, visited));
        }
        return node is JsonArray array && array.Any(child => HasMixedDialectReferences(child, dialect, schemas, visited));
    }
}