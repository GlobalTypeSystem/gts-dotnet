using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>
/// Outcome of casting a stored JSON instance to another <strong>minor</strong> variant of the same GTS type (OP#9).
/// </summary>
public sealed class GtsInstanceCastResult
{
    /// <summary>True when the instance was transformed and validates against the target schema (with GTS <c>const</c> tolerance).</summary>
    public bool Ok { get; init; }

    /// <summary>The instance id passed to the cast (GTS instance id or opaque id).</summary>
    public string? InstanceId { get; init; }

    /// <summary>Schema the instance was bound to before casting.</summary>
    public GtsId? FromSchemaId { get; init; }

    /// <summary>Target schema type id.</summary>
    public GtsId? ToSchemaId { get; init; }

    /// <summary>High-level failure code when <see cref="Ok"/> is false; null on success.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Deep-cloned instance updated toward the target effective schema; null when <see cref="Ok"/> is false.</summary>
    public JsonObject? CastedContent { get; init; }

    /// <summary>
    /// Structural and evolution comparison between source and target schemas; null when schemas could not be compared
    /// (e.g. missing entity).
    /// </summary>
    public GtsMinorVersionPairComparison? Comparison { get; init; }

    /// <summary>Flattened JSON Schema validation messages when <see cref="FailureReason"/> is <c>CastValidationFailed</c>.</summary>
    public IReadOnlyList<string>? SchemaValidationErrors { get; init; }
}
