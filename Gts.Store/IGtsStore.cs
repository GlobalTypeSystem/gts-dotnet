using Gts.Extraction;

namespace Gts.Store;

/// <summary>Outcome of an atomic conditional save (<see cref="IGtsStore.TrySaveAsync"/>).</summary>
public enum GtsSaveOutcome
{
    /// <summary>The entity was inserted (no prior entity with the same id).</summary>
    Added,

    /// <summary>An entity with the same id already existed with identical content; the store was left unchanged.</summary>
    Unchanged,

    /// <summary>An entity with the same id already existed with different content; the store was left unchanged.</summary>
    Conflict
}

/// <summary>Storage abstraction for GTS JSON entities keyed by GTS ID.</summary>
public interface IGtsStore
{
    /// <summary>Stores or overwrites the entity (keyed by its GTS ID).</summary>
    ValueTask SaveAsync(GtsJsonEntity entity);

    /// <summary>
    /// Atomically stores the entity unless an entity with the same id already exists with different content.
    /// This is a single compare-and-swap that closes the check-then-save race: concurrent callers writing
    /// different content for the same id cannot both succeed.
    /// </summary>
    ValueTask<GtsSaveOutcome> TrySaveAsync(GtsJsonEntity entity);
    
    /// <summary>Retrieves an entity by GTS ID, or null if not found.</summary>
    ValueTask<GtsJsonEntity?> GetAsync(GtsId id);

    /// <summary>
    /// Looks up an instance by GTS instance id or by an opaque id (e.g. UUID for anonymous instances).
    /// </summary>
    ValueTask<GtsJsonEntity?> GetByInstanceIdAsync(string instanceId);
    
    /// <summary>Returns all stored entities, including instances without a <see cref="GtsJsonEntity.GtsId"/> (e.g. opaque ids).</summary>
    ValueTask<IList<GtsJsonEntity>> GetAllAsync();

    /// <summary>
    /// Returns a read-only snapshot of all stored entities <strong>without</strong> deep-cloning their content,
    /// for internal read-only consumers (e.g. validation). The returned entities and their <see cref="GtsJsonEntity.Content"/>
    /// must not be mutated. Prefer this over <see cref="GetAllAsync"/> on hot paths that only read.
    /// </summary>
    ValueTask<IReadOnlyList<GtsJsonEntity>> SnapshotForReadAsync();
    
    /// <summary>Returns the number of stored entities.</summary>
    ValueTask<int> CountAsync();
}
