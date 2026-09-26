using System.Collections.Generic;
using System.Text.Json.Nodes;
using Gts.Extraction;

namespace Gts.Store.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IGtsStore"/>. All access is serialized through a single
/// lock, so plain dictionaries are sufficient (a concurrent collection would add overhead without changing behavior).
/// </summary>
internal sealed class InMemoryGtsStore : IGtsStore
{
    private readonly Dictionary<GtsId, GtsJsonEntity> _entities = new();
    private readonly Dictionary<string, GtsJsonEntity> _instanceKeys = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <inheritdoc/>
    public ValueTask SaveAsync(GtsJsonEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var key = ResolveKey(entity);
        var stored = entity.DeepClone();
        lock (_sync)
        {
            StoreLocked(stored, key);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<GtsSaveOutcome> TrySaveAsync(GtsJsonEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var key = ResolveKey(entity);
        var stored = entity.DeepClone();
        lock (_sync)
        {
            var existing = FindLocked(stored.GtsId, key);
            if (existing is not null)
                return ValueTask.FromResult(
                    JsonNode.DeepEquals(existing.Content, stored.Content)
                        ? GtsSaveOutcome.Unchanged
                        : GtsSaveOutcome.Conflict);

            StoreLocked(stored, key);
            return ValueTask.FromResult(GtsSaveOutcome.Added);
        }
    }

    private static string ResolveKey(GtsJsonEntity entity)
    {
        var extract = GtsJsonEntity.ExtractId(entity.Content);
        if (string.IsNullOrEmpty(extract.Id))
            throw new ArgumentException("Entity must have a resolvable id (GTS id or opaque id such as a UUID).", nameof(entity));
        return extract.Id;
    }

    private GtsJsonEntity? FindLocked(GtsId? gtsId, string instanceKey)
    {
        if (gtsId is not null && _entities.TryGetValue(gtsId, out var byGts))
            return byGts;
        return _instanceKeys.TryGetValue(instanceKey, out var byKey) ? byKey : null;
    }

    private void StoreLocked(GtsJsonEntity stored, string instanceKey)
    {
        if (stored.GtsId is not null)
        {
            if (_entities.TryGetValue(stored.GtsId, out var previous))
            {
                var previousId = GtsJsonEntity.ExtractId(previous.Content).Id;
                if (!string.IsNullOrEmpty(previousId) && previousId != instanceKey)
                    _instanceKeys.Remove(previousId);
            }
            _entities[stored.GtsId] = stored;
        }

        _instanceKeys[instanceKey] = stored;
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
    public ValueTask<IReadOnlyList<GtsJsonEntity>> SnapshotForReadAsync()
    {
        lock (_sync)
        {
            var seen = new HashSet<GtsJsonEntity>(ReferenceEqualityComparer.Instance);
            var list = new List<GtsJsonEntity>();
            foreach (var e in _instanceKeys.Values)
            {
                if (seen.Add(e))
                    list.Add(e);
            }

            return ValueTask.FromResult<IReadOnlyList<GtsJsonEntity>>(list);
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
