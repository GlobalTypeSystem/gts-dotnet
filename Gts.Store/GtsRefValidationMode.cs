namespace Gts.Store;

/// <summary>
/// Controls how strictly GTS <c>x-gts-ref</c> constraints are checked during schema and instance validation.
/// </summary>
public enum GtsRefValidationMode
{
    /// <summary>Only check that referenced identifiers are syntactically valid; registration is not required.</summary>
    None,

    /// <summary>Require each referenced identifier to resolve to a registered entity, without validating that entity.</summary>
    AnyPresent,

    /// <summary>Require each reference to resolve to a registered entity that is itself valid against its schema.</summary>
    AnyValid,
}

/// <summary>
/// Parsing helpers mapping the wire tokens (<c>none</c>, <c>any-present</c>, <c>any-valid</c>) used by the HTTP API
/// and query string to <see cref="GtsRefValidationMode"/>.
/// </summary>
public static class GtsRefValidationModes
{
    /// <summary>Mode used when a caller does not specify one.</summary>
    public const GtsRefValidationMode Default = GtsRefValidationMode.AnyValid;

    /// <summary>
    /// Attempts to parse a wire token. Null/empty input maps to <see cref="Default"/> and returns <c>true</c>;
    /// an unrecognized non-empty token returns <c>false</c> (with <paramref name="mode"/> set to <see cref="Default"/>).
    /// </summary>
    public static bool TryParse(string? value, out GtsRefValidationMode mode)
    {
        switch (value?.Trim())
        {
            case null or "":
                mode = Default;
                return true;
            case "none":
                mode = GtsRefValidationMode.None;
                return true;
            case "any-present":
                mode = GtsRefValidationMode.AnyPresent;
                return true;
            case "any-valid":
                mode = GtsRefValidationMode.AnyValid;
                return true;
            default:
                mode = Default;
                return false;
        }
    }

    /// <summary>Parses a wire token, falling back to <see cref="Default"/> for empty or unrecognized input.</summary>
    public static GtsRefValidationMode Parse(string? value) => TryParse(value, out var mode) ? mode : Default;

    /// <summary>Returns the canonical wire token for a mode.</summary>
    public static string ToWireValue(this GtsRefValidationMode mode) => mode switch
    {
        GtsRefValidationMode.None => "none",
        GtsRefValidationMode.AnyPresent => "any-present",
        _ => "any-valid",
    };
}
