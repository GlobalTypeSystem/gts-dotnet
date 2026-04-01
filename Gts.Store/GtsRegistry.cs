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
