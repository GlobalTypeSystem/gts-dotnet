using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

internal sealed class GtsCastService(IGtsStore store)
{
    internal async ValueTask<GtsInstanceCastResult> CastAsync(string instanceId, GtsId targetSchemaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return Failure(instanceId, targetSchemaId, GtsValidationFailure.InvalidInstanceId);
        ArgumentNullException.ThrowIfNull(targetSchemaId);
        cancellationToken.ThrowIfCancellationRequested();
        var id = instanceId.Trim();
        var entity = await store.GetByInstanceIdAsync(id).ConfigureAwait(false);
        if (entity is null) return Failure(id, targetSchemaId, GtsValidationFailure.InstanceNotFound);
        if (entity.IsSchema) return Failure(id, targetSchemaId, GtsValidationFailure.NotAnInstance);
        if (!targetSchemaId.IsType) return Failure(id, targetSchemaId, GtsValidationFailure.InvalidTargetSchemaId);

        var targetSchema = await store.GetAsync(targetSchemaId).ConfigureAwait(false);
        if (targetSchema is null || !targetSchema.IsSchema) return Failure(id, targetSchemaId, GtsValidationFailure.TargetSchemaNotFound);
        var extracted = GtsJsonEntity.ExtractId(entity.Content);
        if (string.IsNullOrEmpty(extracted.SchemaId) || !GtsId.TryParse(extracted.SchemaId, out var sourceSchemaId) || sourceSchemaId is null || !sourceSchemaId.IsType)
            return Failure(id, targetSchemaId, GtsValidationFailure.SchemaIdMissing);
        var sourceSchema = await store.GetAsync(sourceSchemaId).ConfigureAwait(false);
        if (sourceSchema is null || !sourceSchema.IsSchema)
            return new GtsInstanceCastResult { Ok = false, InstanceId = id, FromSchemaId = sourceSchemaId, ToSchemaId = targetSchemaId, FailureReason = GtsValidationFailure.SourceSchemaNotFound };

        var sourceDocument = CanonicalSchema(sourceSchema.Content);
        var targetDocument = CanonicalSchema(targetSchema.Content);
        var comparison = GtsSchemaMinorVersionCompatibility.ComparePair(sourceSchemaId, sourceDocument, targetSchemaId, targetDocument);
        if (!comparison.AreMinorVariantPair)
            return new GtsInstanceCastResult { Ok = false, InstanceId = id, FromSchemaId = sourceSchemaId, ToSchemaId = targetSchemaId, FailureReason = GtsValidationFailure.NotMinorVariantPair, Comparison = comparison };

        var entities = await store.SnapshotForReadAsync().ConfigureAwait(false);
        var schemaById = new Dictionary<string, GtsJsonEntity>(StringComparer.Ordinal);
        foreach (var candidate in entities)
        {
            if (candidate.IsSchema && candidate.GtsId is not null)
                schemaById[candidate.GtsId.Id] = candidate;
        }

        JsonObject? Load(GtsId schemaId)
        {
            return schemaById.TryGetValue(schemaId.Id, out var stored) && stored.IsSchema
                ? CanonicalSchema(stored.Content)
                : null;
        }
        var targetEffective = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(GtsSchemaDependencyGraph.Resolve(targetDocument, Load));
        var casted = GtsInstanceCast.CastToEffectiveSchema(entity.Content, targetEffective);
        var schemas = entities.Where(candidate => candidate.IsSchema && candidate.GtsId is not null)
            .ToDictionary(candidate => candidate.GtsId!, candidate => GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(candidate.Content));
        if (!schemas.ContainsKey(targetSchemaId))
            return new GtsInstanceCastResult { Ok = false, InstanceId = id, FromSchemaId = sourceSchemaId, ToSchemaId = targetSchemaId, FailureReason = GtsValidationFailure.SchemaNormalizationFailed, Comparison = comparison, CastedContent = casted };

        var tolerant = (JsonObject)GtsInstanceCast.RemoveGtsConstConstraints(targetDocument.DeepClone())!.AsObject();
        schemas[targetSchemaId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(tolerant);
        var evaluation = GtsJsonSchemaEvaluator.Evaluate(casted, targetSchemaId, schemas);
        if (!evaluation.IsValid)
            return new GtsInstanceCastResult { Ok = false, InstanceId = id, FromSchemaId = sourceSchemaId, ToSchemaId = targetSchemaId, FailureReason = GtsValidationFailure.CastValidationFailed, Comparison = comparison, CastedContent = casted, SchemaValidationErrors = GtsJsonSchemaEvaluator.FlattenErrors(evaluation) };
        return new GtsInstanceCastResult { Ok = true, InstanceId = id, FromSchemaId = sourceSchemaId, ToSchemaId = targetSchemaId, CastedContent = casted, Comparison = comparison };
    }

    private static JsonObject CanonicalSchema(JsonObject schema) =>
        !schema.ContainsKey("$schema") && schema.ContainsKey("$$schema")
            ? GtsSchemaDocumentNormalizer.CanonicalizeKeywords(schema)
            : schema;

    private static GtsInstanceCastResult Failure(string? id, GtsId target, GtsValidationFailure reason) => new() { Ok = false, InstanceId = id, ToSchemaId = target, FailureReason = reason };
}