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
    private readonly GtsSchemaValidationService _schemaValidation;
    private readonly GtsInstanceValidationService _instanceValidation;
    private readonly GtsCastService _casting;

    /// <summary>Registry configuration (e.g. reference validation).</summary>
    public GtsRegistryConfig Config { get; }

    /// <summary>Initializes the registry with the given store and config.</summary>
    protected GtsRegistry(IGtsStore store, GtsRegistryConfig config)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(config);

        _store = store;
        Config = config;
        _schemaValidation = new GtsSchemaValidationService(store);
        _instanceValidation = new GtsInstanceValidationService(store, (id, token, mode) => _schemaValidation.ValidateStoredAsync(id.Id, token, mode));
        _casting = new GtsCastService(store);
    }

    /// <summary>Stores or overwrites the entity in the registry.</summary>
    public ValueTask SaveAsync(GtsJsonEntity entity)
    {
        return _store.SaveAsync(entity);
    }

    /// <summary>
    /// Atomically stores the entity unless one with the same id already exists with different content.
    /// Returns <see cref="GtsSaveOutcome.Conflict"/> in that case without mutating the store.
    /// </summary>
    public ValueTask<GtsSaveOutcome> TrySaveAsync(GtsJsonEntity entity)
    {
        return _store.TrySaveAsync(entity);
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

    /// <summary>
    /// Returns a read-only snapshot of all entities without deep-cloning their content. The returned entities
    /// must be treated as read-only; intended for internal read-only consumers such as validation.
    /// </summary>
    public ValueTask<IReadOnlyList<GtsJsonEntity>> SnapshotForReadAsync()
    {
        return _store.SnapshotForReadAsync();
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

        // Read-only snapshot avoids cloning the whole registry up front; GtsQuery.Execute deep-clones
        // only the entities that actually match (single clone per result instead of one per entity).
        var all = await _store.SnapshotForReadAsync().ConfigureAwait(false);
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
    public ValueTask<GtsInstanceValidationResult> ValidateInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default) =>
        _instanceValidation.ValidateStoredAsync(instanceId, cancellationToken, refValidationMode);

    public ValueTask<GtsInstanceValidationResult> ValidateJsonAsync(
        JsonObject content,
        GtsId schemaGtsId,
        string? instanceId = null,
        CancellationToken cancellationToken = default,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default) =>
        _instanceValidation.ValidateAsync(content, schemaGtsId, instanceId, cancellationToken, refValidationMode);

    /// <summary>
    /// Validates a stored schema: <c>$ref</c> must be local (<c>#</c>) or <c>gts://</c>, and the schema must be
    /// forward-compatible with each precedent type in its GTS id chain (immediate parent, then grandparent, …).
    /// </summary>
    /// <param name="schemaTypeId">GTS type id of the schema (trailing <c>~</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask<GtsSchemaValidationResult> ValidateSchemaAsync(
        string schemaTypeId,
        CancellationToken cancellationToken = default,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default) =>
        _schemaValidation.ValidateStoredAsync(schemaTypeId, cancellationToken, refValidationMode);

    /// <summary>
    /// Validates a schema document for a GTS type id using stored precedent schemas only (the document itself need not be in the registry).
    /// </summary>
    /// <param name="schemaTypeId">GTS type id this document defines (trailing <c>~</c>).</param>
    /// <param name="schemaDocument">JSON Schema body (e.g. from extraction).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask<GtsSchemaValidationResult> ValidateSchemaAsync(
        GtsId schemaTypeId,
        JsonObject schemaDocument,
        CancellationToken cancellationToken = default,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default) =>
        _schemaValidation.ValidateAsync(schemaTypeId, schemaDocument, cancellationToken, refValidationMode);

    /// <summary>
    /// Validates a stored schema by type id (see <see cref="ValidateSchemaAsync(string, CancellationToken)"/>).
    /// </summary>
    public ValueTask<GtsSchemaValidationResult> ValidateSchemaAsync(
        GtsId schemaTypeId,
        CancellationToken cancellationToken = default,
        GtsRefValidationMode refValidationMode = GtsRefValidationModes.Default)
    {
        ArgumentNullException.ThrowIfNull(schemaTypeId);
        return ValidateSchemaAsync(schemaTypeId.Id, cancellationToken, refValidationMode);
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
    public ValueTask<GtsInstanceCastResult> CastInstanceAsync(
        string instanceId,
        GtsId toSchemaId,
        CancellationToken cancellationToken = default) =>
        _casting.CastAsync(instanceId, toSchemaId, cancellationToken);

    /// <summary>Creates an in-memory registry. The in-memory store is thread-safe.</summary>
    public static GtsRegistry InMemory(GtsRegistryConfig config)
    {
        return InMemoryGtsRegistry.Create(config);
    }

    /// <summary>
    /// Creates an in-memory registry. Retained for API compatibility; the in-memory store is always
    /// thread-safe, so this is equivalent to <see cref="InMemory"/>.
    /// </summary>
    public static GtsRegistry InMemoryThreadSafe(GtsRegistryConfig config)
    {
        return InMemoryGtsRegistry.Create(config);
    }
}
