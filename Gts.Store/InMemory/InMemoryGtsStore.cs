using System.Collections.Concurrent;
using System.Collections.Generic;
using Gts.Extraction;

namespace Gts.Store.InMemory;

/// <summary>In-memory implementation of <see cref="IGtsStore"/> using a dictionary-like backing store.</summary>
internal class InMemoryGtsStore<T> : IGtsStore
    where T : class, IDictionary<GtsId, GtsJsonEntity>, new()
{
    private readonly T _entities = new();
    private readonly ConcurrentDictionary<string, GtsJsonEntity> _instanceKeys = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <inheritdoc/>
    public ValueTask SaveAsync(GtsJsonEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var extract = GtsJsonEntity.ExtractId(entity.Content);
        if (string.IsNullOrEmpty(extract.Id))
            throw new ArgumentException("Entity must have a resolvable id (GTS id or opaque id such as a UUID).", nameof(entity));

        var stored = entity.DeepClone();
        lock (_sync)
        {
            if (stored.GtsId is not null)
            {
                if (_entities.TryGetValue(stored.GtsId, out var previous))
                {
                    var previousId = GtsJsonEntity.ExtractId(previous.Content).Id;
                    if (!string.IsNullOrEmpty(previousId) && previousId != extract.Id)
                        _instanceKeys.TryRemove(previousId, out _);
                }
                _entities[stored.GtsId] = stored;
            }

            _instanceKeys[extract.Id] = stored;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<GtsJsonEntity?> GetAsync(GtsId id)
    {
        lock (_sync)
        {
            _entities.TryGetValue(id, out var entity);
            return ValueTask.FromResult(entity?.DeepClone());
        }
    }

    /// <inheritdoc/>
    public ValueTask<GtsJsonEntity?> GetByInstanceIdAsync(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ValueTask.FromResult<GtsJsonEntity?>(null);

        var trimmed = instanceId.Trim();
        lock (_sync)
        {
            if (GtsId.TryParse(trimmed, out var gid) && gid is not null && _entities.TryGetValue(gid, out var byGts))
                return ValueTask.FromResult<GtsJsonEntity?>(byGts.DeepClone());

            _instanceKeys.TryGetValue(trimmed, out var byKey);
            return ValueTask.FromResult(byKey?.DeepClone());
        }
    }

    /// <inheritdoc/>
    public ValueTask<IList<GtsJsonEntity>> GetAllAsync()
    {
        lock (_sync)
        {
            var seen = new HashSet<GtsJsonEntity>(ReferenceEqualityComparer.Instance);
            var list = new List<GtsJsonEntity>();
            foreach (var e in _instanceKeys.Values)
            {
                if (seen.Add(e))
                    list.Add(e.DeepClone());
            }

            return ValueTask.FromResult<IList<GtsJsonEntity>>(list);
        }
    }

    /// <inheritdoc/>
    public ValueTask<int> CountAsync()
    {
        lock (_sync)
        {
            var seen = new HashSet<GtsJsonEntity>(ReferenceEqualityComparer.Instance);
            foreach (var e in _instanceKeys.Values)
                seen.Add(e);
            return ValueTask.FromResult(seen.Count);
        }
    }
}
