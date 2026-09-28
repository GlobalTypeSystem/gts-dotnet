namespace Gts;

/// <summary>
/// Central definitions for the GTS identifier prefix, the <c>gts://</c> URI form, and shared
/// traversal limits. Centralizing these avoids the magic literals (notably the <c>[6..]</c> slice
/// that hard-codes <c>"gts://".Length</c>) that were previously scattered across the codebase.
/// </summary>
public static class GtsConstants
{
    /// <summary>The canonical GTS identifier prefix (every GTS id starts with this).</summary>
    public const string IdPrefix = "gts.";

    /// <summary>The <c>gts://</c> URI form used in JSON Schema <c>$id</c>/<c>$ref</c> values.</summary>
    public const string UriPrefix = "gts://";

    /// <summary>
    /// Maximum nesting depth for recursive traversal of untrusted JSON/JSON Schema documents.
    /// Acts as a backstop against <see cref="System.StackOverflowException"/> from pathologically or
    /// maliciously deep input; chosen high enough not to reject realistic documents.
    /// </summary>
    public const int MaxNestingDepth = 512;

    /// <summary>
    /// Returns <paramref name="value"/> with a leading <see cref="UriPrefix"/> removed, or the value
    /// unchanged when it does not start with the prefix.
    /// </summary>
    public static string StripUriPrefix(string value) =>
        value.StartsWith(UriPrefix, System.StringComparison.Ordinal) ? value[UriPrefix.Length..] : value;
}
