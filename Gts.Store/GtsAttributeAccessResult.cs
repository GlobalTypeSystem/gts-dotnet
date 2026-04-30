using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Outcome of attribute resolution (<c>id@path</c>, OP#11).</summary>
public sealed record GtsAttributeAccessResult(
    bool Resolved,
    JsonNode? Value,
    string? InstanceId,
    string? AttributePath,
    string? Error,
    IReadOnlyList<string>? AvailableFields);
