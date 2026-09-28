using System.Text.Json.Nodes;

namespace Gts.Store;

public static class GtsSchemaCompatibilityService
{
    internal static IReadOnlyList<string> ValidateDerivation(JsonObject parent, JsonObject child) =>
        GtsSchemaDerivationValidator.ValidateCompatibilityCore(parent, child);

    internal static IReadOnlyList<string> ValidateTraitOverlay(JsonObject parent, JsonObject overlay) =>
        GtsSchemaDerivationValidator.ValidateOverlayCore(parent, overlay);

    public static (bool Backward, IReadOnlyList<string> BackwardErrors, bool Forward, IReadOnlyList<string> ForwardErrors)
        CompareEvolution(JsonObject oldSchema, JsonObject newSchema)
    {
        var (backward, backwardErrors) = GtsJsonSchemaEvolutionCompatibility.CheckBackward(oldSchema, newSchema);
        var (forward, forwardErrors) = GtsJsonSchemaEvolutionCompatibility.CheckForward(oldSchema, newSchema);
        return (backward, backwardErrors, forward, forwardErrors);
    }
}