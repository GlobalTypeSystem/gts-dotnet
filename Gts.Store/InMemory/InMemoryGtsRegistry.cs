using System.Collections.Concurrent;
using Gts.Extraction;

namespace Gts.Store.InMemory;

/// <summary>In-memory implementation of <see cref="GtsRegistry"/>.</summary>
internal class InMemoryGtsRegistry : GtsRegistry
{
    private InMemoryGtsRegistry(IGtsStore store, GtsRegistryConfig config)
        : base(store, config)
    {
    }

    /// <summary>Creates a simple (non-concurrent) in-memory registry.</summary>
    internal static InMemoryGtsRegistry Simple(GtsRegistryConfig config)
    {
        return new InMemoryGtsRegistry(
            new InMemoryGtsStore<Dictionary<GtsId, GtsJsonEntity>>(), config);
    }

    /// <summary>Creates a thread-safe in-memory registry using a concurrent dictionary.</summary>
    internal static InMemoryGtsRegistry Concurrent(GtsRegistryConfig config)
    {
        return new InMemoryGtsRegistry(
            new InMemoryGtsStore<ConcurrentDictionary<GtsId, GtsJsonEntity>>(), config);
    }
}
