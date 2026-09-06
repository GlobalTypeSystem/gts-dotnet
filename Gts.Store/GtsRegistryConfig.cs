namespace Gts.Store;

/// <summary>Configuration for a GTS registry.</summary>
/// <param name="ValidateGtsReferences">When true, validate GTS references on save.</param>
public record GtsRegistryConfig(
    bool ValidateGtsReferences
);
