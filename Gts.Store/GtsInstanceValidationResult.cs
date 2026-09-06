namespace Gts.Store;

/// <summary>
/// Outcome of validating a JSON instance against its resolved GTS JSON Schema (OP#6).
/// </summary>
public sealed class GtsInstanceValidationResult
{
    /// <summary>True when the instance exists, is not a schema document, and conforms to its schema.</summary>
    public bool Ok { get; init; }

    /// <summary>The instance id that was validated (GTS instance id or opaque id such as a UUID).</summary>
    public string? Id { get; init; }

    /// <summary>High-level failure code when <see cref="Ok"/> is false; null on success.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Flattened JSON Schema validation messages when <see cref="FailureReason"/> is <c>SchemaValidationFailed</c>.</summary>
    public IReadOnlyList<string>? SchemaErrors { get; init; }
}
