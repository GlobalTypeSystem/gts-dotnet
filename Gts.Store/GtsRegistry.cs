using System.Text.Json;
using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.InMemory;
using Gts.Store.Validation;

namespace Gts.Store;

/// <summary>Registry for GTS JSON entities with configurable storage and validation.</summary>
public abstract class GtsRegistry
{
    private readonly IGtsStore _store;
    
    /// <summary>Registry configuration (e.g. reference validation).</summary>
    public GtsRegistryConfig Config { get; }

    /// <summary>Initializes the registry with the given store and config.</summary>
    protected GtsRegistry(IGtsStore store, GtsRegistryConfig config)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(config);
        
        _store = store;
        Config = config;
    }
    
    /// <summary>Stores or overwrites the entity in the registry.</summary>
    public ValueTask SaveAsync(GtsJsonEntity entity)
    {
        // TODO: validation logic
        return _store.SaveAsync(entity);
    }

    /// <summary>Retrieves an entity by GTS ID, or null if not found.</summary>
    public ValueTask<GtsJsonEntity?> GetAsync(GtsId id)
    {
        return _store.GetAsync(id);
    }

    /// <summary>
    /// Looks up an instance by GTS instance id or by an opaque id (e.g. UUID for anonymous instances).
    /// </summary>
    public ValueTask<GtsJsonEntity?> GetByInstanceIdAsync(string instanceId)
    {
        return _store.GetByInstanceIdAsync(instanceId);
    }

    /// <summary>Returns all entities in the registry.</summary>
    public ValueTask<IList<GtsJsonEntity>> GetAllAsync()
    {
        return _store.GetAllAsync();
    }

    /// <summary>Returns the number of entities in the registry.</summary>
    public ValueTask<int> CountAsync()
    {
        return _store.CountAsync();
    }

    /// <summary>
    /// Executes a GTS query expression over stored entities: exact or wildcard id pattern, optional JSON attribute filters (AND).
    /// Limit is accepted when in the range 1–1000; otherwise 100 is used (same as <c>GET /query</c>).
    /// </summary>
    /// <param name="expr">Query string, e.g. <c>gts.vendor.pkg.*</c> or <c>gts.vendor.pkg.ns.type.v1~x._.inst.v1.0[status=active]</c>.</param>
    /// <param name="limit">Maximum number of matching entity bodies to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsQueryExecutionResult> QueryAsync(
        string expr,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lim = GtsQuery.NormalizeLimit(limit);
        if (!GtsQuery.TryParse(expr, out var parsed, out var parseError) || parsed is null)
            return GtsQueryExecutionResult.Failed(lim, parseError ?? "Invalid query");

        var all = await _store.GetAllAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var results = GtsQuery.Execute(all, parsed, lim, cancellationToken);
        return GtsQueryExecutionResult.Success(lim, results);
    }

    /// <summary>
    /// Resolves a single value from a stored entity using the attribute selector: <c>&lt;instance id&gt;@json.path</c>
    /// (see GTS spec, attribute selector). <paramref name="gtsWithPath"/> must contain <c>@</c>; path uses dot notation; <c>/</c> is equivalent to <c>.</c>.
    /// The instance is loaded by <see cref="GetByInstanceIdAsync"/> (GTS instance id or opaque id).
    /// </summary>
    /// <param name="gtsWithPath">For example <c>gts.vendor.pkg.ns.type.v1~x._.inst.v1.0@payload.orderId</c>.</param>
    public async ValueTask<GtsAttributeAccessResult> GetAttributeAsync(
        string gtsWithPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (idPart, pathPart) = GtsAttributeSelector.SplitGtsWithPath(gtsWithPath);
        if (pathPart is null)
        {
            return new GtsAttributeAccessResult(
                false,
                null,
                idPart,
                null,
                "Attribute selector requires '@path' in the identifier.",
                null);
        }

        if (string.IsNullOrWhiteSpace(idPart))
        {
            return new GtsAttributeAccessResult(
                false,
                null,
                null,
                pathPart,
                "Missing GTS identifier before '@'.",
                null);
        }

        var trimmedId = idPart.Trim();
        var entity = await _store.GetByInstanceIdAsync(trimmedId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (entity is null)
        {
            return new GtsAttributeAccessResult(
                false,
                null,
                trimmedId,
                pathPart,
                "Entity not found.",
                null);
        }

        if (!GtsAttributeSelector.TryResolvePath(entity.Content, pathPart, out var val, out var err, out var fields))
        {
            return new GtsAttributeAccessResult(
                false,
                null,
                trimmedId,
                pathPart,
                err,
                fields);
        }

        return new GtsAttributeAccessResult(true, val, trimmedId, pathPart, null, null);
    }

    /// <summary>
    /// Validates a stored instance against the JSON Schema for its resolved type (rightmost type in the id chain, or <c>type</c> for anonymous instances).
    /// </summary>
    /// <param name="instanceId">GTS instance id or opaque id (e.g. UUID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsInstanceValidationResult> ValidateInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(instanceId))
            return new GtsInstanceValidationResult { Ok = false, Id = instanceId };

        var trimmed = instanceId.Trim();
        var entity = await _store.GetByInstanceIdAsync(trimmed).ConfigureAwait(false);
        if (entity is null)
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "InstanceNotFound" };

        if (entity.IsSchema)
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "NotAnInstance" };

        var extract = GtsJsonEntity.ExtractId(entity.Content);
        var schemaIdStr = extract.SchemaId;
        if (string.IsNullOrEmpty(schemaIdStr) || !schemaIdStr.EndsWith('~'))
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "SchemaIdMissing" };

        if (!GtsId.TryParse(schemaIdStr, out var schemaGtsId) || schemaGtsId is null || !schemaGtsId.IsType)
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "InvalidSchemaId" };

        var schemaEntity = await _store.GetAsync(schemaGtsId).ConfigureAwait(false);
        if (schemaEntity is null || !schemaEntity.IsSchema)
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "SchemaNotFound" };

        var all = await _store.GetAllAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedMap = new Dictionary<GtsId, JsonObject>();
        foreach (var e in all)
        {
            if (!e.IsSchema || e.GtsId is null)
                continue;
            normalizedMap[e.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(e.Content);
        }

        if (!normalizedMap.ContainsKey(schemaGtsId))
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "SchemaNotFound" };

        JsonDocument instDoc;
        try
        {
            instDoc = JsonDocument.Parse(entity.Content.ToJsonString());
        }
        catch (JsonException)
        {
            return new GtsInstanceValidationResult { Ok = false, Id = trimmed, FailureReason = "InvalidInstanceJson" };
        }

        using (instDoc)
        {
            var results = GtsJsonSchemaEvaluator.Evaluate(instDoc.RootElement, schemaGtsId, normalizedMap);

            if (results.IsValid)
                return new GtsInstanceValidationResult { Ok = true, Id = trimmed };

            return new GtsInstanceValidationResult
            {
                Ok = false,
                Id = trimmed,
                FailureReason = "SchemaValidationFailed",
                SchemaErrors = GtsJsonSchemaEvaluator.FlattenErrors(results)
            };
        }
    }

    /// <summary>
    /// Loads every stored entity, walks GTS references in each document, and reports references that do not
    /// resolve to another stored schema (type id) or instance. Pattern identifiers are ignored.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsRelationshipResolutionResult> ResolveRelationshipsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = await _store.GetAllAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return GtsRelationshipResolver.Analyze(all);
    }

    /// <summary>
    /// Verifies <strong>full</strong> compatibility between stored schemas whose GTS type ids differ only in the
    /// last segment's minor version: after normalizing Draft-07 and GTS URIs, the schema trees must be identical.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsMinorVersionCompatibilityReport> CheckMinorVersionCompatibilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = await _store.GetAllAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return GtsSchemaMinorVersionCompatibility.AnalyzeStoredSchemas(all);
    }

    /// <summary>
    /// Loads two schema entities and compares them for structural minor compatibility and JSON Schema evolution
    /// (backward / forward), ordered by last-segment minor version.
    /// </summary>
    /// <param name="schemaIdA">First schema type id.</param>
    /// <param name="schemaIdB">Second schema type id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsMinorVersionPairComparison> CompareMinorVersionSchemasAsync(
        GtsId schemaIdA,
        GtsId schemaIdB,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaIdA);
        ArgumentNullException.ThrowIfNull(schemaIdB);

        cancellationToken.ThrowIfCancellationRequested();
        var a = await GetAsync(schemaIdA).ConfigureAwait(false);
        var b = await GetAsync(schemaIdB).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (a is null || b is null || !a.IsSchema || !b.IsSchema)
        {
            return new GtsMinorVersionPairComparison
            {
                AreMinorVariantPair = false,
                IsStructurallyCompatible = false,
                StructuralIncompatibilityReason = "One or both schemas are missing or not schema documents.",
                IsBackwardEvolutionCompatible = false,
                BackwardEvolutionErrors = new[] { "Evolution checks require two stored schema entities." },
                IsForwardEvolutionCompatible = false,
                ForwardEvolutionErrors = new[] { "Evolution checks require two stored schema entities." }
            };
        }

        return GtsSchemaMinorVersionCompatibility.ComparePair(schemaIdA, a.Content, schemaIdB, b.Content);
    }

    /// <summary>
    /// Transforms a stored instance toward a target type id that is a <strong>minor</strong> variant of the instance&apos;s
    /// schema (same GTS type family). Fills defaults, updates GTS id <c>const</c> fields, prunes when
    /// <c>additionalProperties</c> is false, then validates against the target schema with GTS <c>const</c> tolerance.
    /// </summary>
    /// <param name="instanceId">GTS instance id or opaque id (e.g. UUID).</param>
    /// <param name="toSchemaId">Target schema type id (trailing <c>~</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<GtsInstanceCastResult> CastInstanceAsync(
        string instanceId,
        GtsId toSchemaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = instanceId,
                FailureReason = "InvalidInstanceId"
            };
        }

        ArgumentNullException.ThrowIfNull(toSchemaId);

        cancellationToken.ThrowIfCancellationRequested();
        var trimmed = instanceId.Trim();
        var entity = await _store.GetByInstanceIdAsync(trimmed).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (entity is null)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                ToSchemaId = toSchemaId,
                FailureReason = "InstanceNotFound"
            };
        }

        if (entity.IsSchema)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                ToSchemaId = toSchemaId,
                FailureReason = "NotAnInstance"
            };
        }

        if (!toSchemaId.IsType)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                ToSchemaId = toSchemaId,
                FailureReason = "InvalidTargetSchemaId"
            };
        }

        var toSchemaEntity = await _store.GetAsync(toSchemaId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (toSchemaEntity is null || !toSchemaEntity.IsSchema)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                ToSchemaId = toSchemaId,
                FailureReason = "TargetSchemaNotFound"
            };
        }

        var extract = GtsJsonEntity.ExtractId(entity.Content);
        var fromSchemaIdStr = extract.SchemaId;
        if (string.IsNullOrEmpty(fromSchemaIdStr) || !GtsId.TryParse(fromSchemaIdStr, out var fromGid) || fromGid is null || !fromGid.IsType)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                ToSchemaId = toSchemaId,
                FailureReason = "SchemaIdMissing"
            };
        }

        var fromSchemaEntity = await _store.GetAsync(fromGid).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (fromSchemaEntity is null || !fromSchemaEntity.IsSchema)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                FromSchemaId = fromGid,
                ToSchemaId = toSchemaId,
                FailureReason = "SourceSchemaNotFound"
            };
        }

        var comparison = GtsSchemaMinorVersionCompatibility.ComparePair(
            fromGid,
            fromSchemaEntity.Content,
            toSchemaId,
            toSchemaEntity.Content);

        if (!comparison.AreMinorVariantPair || !EvolutionAllowsCast(fromGid, toSchemaId, comparison))
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                FromSchemaId = fromGid,
                ToSchemaId = toSchemaId,
                FailureReason = !comparison.AreMinorVariantPair ? "NotMinorVariantPair" : "IncompatibleMinorEvolution",
                Comparison = comparison
            };
        }

        var targetFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(toSchemaEntity.Content);
        var casted = GtsInstanceCast.CastToEffectiveSchema(entity.Content, targetFlat);

        var all = await _store.GetAllAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedMap = new Dictionary<GtsId, JsonObject>();
        foreach (var e in all)
        {
            if (!e.IsSchema || e.GtsId is null)
                continue;
            normalizedMap[e.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(e.Content);
        }

        if (!normalizedMap.ContainsKey(toSchemaId))
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                FromSchemaId = fromGid,
                ToSchemaId = toSchemaId,
                FailureReason = "SchemaNormalizationFailed",
                Comparison = comparison,
                CastedContent = casted
            };
        }

        var tolerant = (JsonObject)GtsInstanceCast.RemoveGtsConstConstraints(
            JsonNode.Parse(toSchemaEntity.Content.ToJsonString())!)!.AsObject();
        normalizedMap[toSchemaId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(tolerant);

        JsonDocument instDoc;
        try
        {
            instDoc = JsonDocument.Parse(casted.ToJsonString());
        }
        catch (JsonException)
        {
            return new GtsInstanceCastResult
            {
                Ok = false,
                InstanceId = trimmed,
                FromSchemaId = fromGid,
                ToSchemaId = toSchemaId,
                FailureReason = "InvalidCastedJson",
                Comparison = comparison,
                CastedContent = casted
            };
        }

        using (instDoc)
        {
            var eval = GtsJsonSchemaEvaluator.Evaluate(instDoc.RootElement, toSchemaId, normalizedMap);
            if (!eval.IsValid)
            {
                return new GtsInstanceCastResult
                {
                    Ok = false,
                    InstanceId = trimmed,
                    FromSchemaId = fromGid,
                    ToSchemaId = toSchemaId,
                    FailureReason = "CastValidationFailed",
                    Comparison = comparison,
                    CastedContent = casted,
                    SchemaValidationErrors = GtsJsonSchemaEvaluator.FlattenErrors(eval)
                };
            }
        }

        return new GtsInstanceCastResult
        {
            Ok = true,
            InstanceId = trimmed,
            FromSchemaId = fromGid,
            ToSchemaId = toSchemaId,
            CastedContent = casted,
            Comparison = comparison
        };
    }

    private static bool EvolutionAllowsCast(GtsId fromSchemaId, GtsId toSchemaId, GtsMinorVersionPairComparison cmp)
    {
        if (!cmp.AreMinorVariantPair || cmp.OlderSchemaId is null || cmp.NewerSchemaId is null)
            return false;

        if (string.Equals(fromSchemaId.Id, toSchemaId.Id, StringComparison.Ordinal))
            return true;

        var fromIsOlder = string.Equals(fromSchemaId.Id, cmp.OlderSchemaId.Id, StringComparison.Ordinal);
        var toIsNewer = string.Equals(toSchemaId.Id, cmp.NewerSchemaId.Id, StringComparison.Ordinal);
        if (fromIsOlder && toIsNewer)
            return cmp.IsBackwardEvolutionCompatible;

        var fromIsNewer = string.Equals(fromSchemaId.Id, cmp.NewerSchemaId.Id, StringComparison.Ordinal);
        var toIsOlder = string.Equals(toSchemaId.Id, cmp.OlderSchemaId.Id, StringComparison.Ordinal);
        if (fromIsNewer && toIsOlder)
            return cmp.IsForwardEvolutionCompatible;

        return false;
    }

    /// <summary>Creates an in-memory registry (single-threaded).</summary>
    public static GtsRegistry InMemory(GtsRegistryConfig config)
    {
        return InMemoryGtsRegistry.Simple(config);
    }

    /// <summary>Creates an in-memory registry with thread-safe storage.</summary>
    public static GtsRegistry InMemoryThreadSafe(GtsRegistryConfig config)
    {
        return InMemoryGtsRegistry.Concurrent(config);
    }
}
