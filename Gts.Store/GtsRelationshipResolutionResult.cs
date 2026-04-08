namespace Gts.Store;

/// <summary>
/// Outcome of loading all registry entities and checking cross-references between schemas and instances.
/// </summary>
public sealed class GtsRelationshipResolutionResult
{
    /// <summary>Total entities returned by the store (schemas and instances, including anonymous instances).</summary>
    public required int EntityCount { get; init; }

    /// <summary>Entities classified as JSON Schemas (<see cref="Gts.Extraction.GtsJsonEntity.IsSchema"/>).</summary>
    public required int SchemaCount { get; init; }

    /// <summary>Entities that are not JSON Schemas.</summary>
    public required int InstanceCount { get; init; }

    /// <summary>References that do not resolve to a stored schema or instance.</summary>
    public required IReadOnlyList<GtsBrokenReference> BrokenReferences { get; init; }

    /// <summary>True when there are no broken references.</summary>
    public bool IsConsistent => BrokenReferences.Count == 0;
}
