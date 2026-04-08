namespace Gts.Store;

/// <summary>
/// A GTS identifier referenced from a stored entity that does not resolve to another stored entity.
/// </summary>
/// <param name="SourceId">Primary id of the referencing entity (GTS id or opaque id such as a UUID).</param>
/// <param name="SourceIsSchema">True if the source document is a JSON Schema.</param>
/// <param name="ReferencedId">The GTS identifier that could not be resolved.</param>
/// <param name="SourcePath">JSON path where the reference was found (see <see cref="Gts.Extraction.GtsReference"/>).</param>
/// <param name="Reason">Machine-readable reason, e.g. <c>MissingSchema</c>, <c>MissingInstance</c>, <c>MissingTypeBinding</c>.</param>
public sealed record GtsBrokenReference(
    string SourceId,
    bool SourceIsSchema,
    string ReferencedId,
    string SourcePath,
    string Reason);
