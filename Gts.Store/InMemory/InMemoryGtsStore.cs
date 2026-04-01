using System.Collections.Concurrent;
using Gts.Extraction;

namespace Gts.Store.InMemory;

/// <summary>In-memory implementation of <see cref="IGtsStore"/> using a dictionary-like backing store.</summary>
internal class InMemoryGtsStore<T> : IGtsStore
    where T : class, IDictionary<GtsId, GtsJsonEntity>, new()
{
    private readonly T _entities = new();
    private readonly ConcurrentDictionary<string, GtsJsonEntity> _instanceKeys = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask SaveAsync(GtsJsonEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var extract = GtsJsonEntity.ExtractId(entity.Content);
        if (string.IsNullOrEmpty(extract.Id))
            throw new ArgumentException("Entity must have a resolvable id (GTS id or opaque id such as a UUID).", nameof(entity));

        if (entity.GtsId is not null)
            _entities[entity.GtsId] = entity;

        _instanceKeys[extract.Id] = entity;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<GtsJsonEntity?> GetAsync(GtsId id)
    {
        _entities.TryGetValue(id, out var entity);
        return ValueTask.FromResult(entity);
    }

    /// <inheritdoc/>
    public ValueTask<GtsJsonEntity?> GetByInstanceIdAsync(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ValueTask.FromResult<GtsJsonEntity?>(null);

        var trimmed = instanceId.Trim();
        if (GtsId.TryParse(trimmed, out var gid) && gid is not null && _entities.TryGetValue(gid, out var byGts))
            return ValueTask.FromResult<GtsJsonEntity?>(byGts);

        _instanceKeys.TryGetValue(trimmed, out var byKey);
        return ValueTask.FromResult(byKey);
    }

    /// <inheritdoc/>
    public ValueTask<IList<GtsJsonEntity>> GetAllAsync()
    {
        return ValueTask.FromResult<IList<GtsJsonEntity>>(_entities.Values.ToArray());
    }

    /// <inheritdoc/>
    public ValueTask<int> CountAsync()
    {
        return ValueTask.FromResult(_entities.Count);
    }
}
