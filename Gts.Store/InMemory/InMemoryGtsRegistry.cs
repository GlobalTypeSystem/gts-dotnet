using Gts.Extraction;

namespace Gts.Store.InMemory;

/// <summary>In-memory implementation of <see cref="GtsRegistry"/> backed by a thread-safe store.</summary>
internal sealed class InMemoryGtsRegistry : GtsRegistry
{
    private InMemoryGtsRegistry(IGtsStore store, GtsRegistryConfig config)
        : base(store, config)
    {
    }

    /// <summary>Creates an in-memory registry backed by the thread-safe in-memory store.</summary>
    internal static InMemoryGtsRegistry Create(GtsRegistryConfig config)
    {
        return new InMemoryGtsRegistry(new InMemoryGtsStore(), config);
    }
}
