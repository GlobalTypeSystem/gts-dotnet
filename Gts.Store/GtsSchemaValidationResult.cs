namespace Gts.Store;

/// <summary>
/// Outcome of validating a stored JSON Schema against the GTS type-chain precedent (parent type) and ref rules (OP#12).
/// </summary>
public sealed class GtsSchemaValidationResult
{
    /// <summary>True when the schema exists, is a schema document, and is forward-compatible with every precedent in its type id chain.</summary>
    public bool Ok { get; init; }

    /// <summary>The GTS type id of the schema that was validated (trailing <c>~</c>).</summary>
    public string? SchemaId { get; init; }

    /// <summary>High-level failure code when <see cref="Ok"/> is false; null on success.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Detailed messages: JSON Schema evolution violations vs a precedent, or a single ref-format error.
    /// </summary>
    public IReadOnlyList<string>? Errors { get; init; }
}
