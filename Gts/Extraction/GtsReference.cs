namespace Gts.Extraction;

/// <summary>
/// A GTS ID found somewhere in a JSON tree, with its JSON path (e.g. "$id", "properties.x.$ref").
/// </summary>
/// <param name="Id">The GTS identifier string.</param>
/// <param name="SourcePath">JSON path where the ID was found; "root" for root-level.</param>
public sealed record GtsReference(string Id, string SourcePath);
