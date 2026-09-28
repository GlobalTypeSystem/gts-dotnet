using Pidgin;

namespace Gts.Parsing;

internal sealed record ParsedGtsId(string CanonicalId, IReadOnlyList<GtsIdSegment> Segments, bool IsType, bool HasUuidTail);

internal static class GtsIdParser
{
    internal static bool TryParse(string? input, int maxLength, out ParsedGtsId? parsed)
    {
        parsed = null;
        if (input is null)
            return false;
        // Validate the original input: a GtsId is always the bare canonical form ("gts.…").
        // The "gts://" URI form is a JSON Schema serialization detail ($id/$ref) and is stripped
        // by those URI-specific callers before they reach here, matching the gts-rust/gts-go
        // reference implementations. Accepting it here makes GtsId.Id disagree with the input and
        // lets URI-form values leak into query/validation paths that compare against canonical ids.
        var value = input;
        if (value.Length == 0 || value.Length > maxLength || value != value.ToLowerInvariant() || !value.StartsWith(GtsConstants.IdPrefix, StringComparison.Ordinal))
            return false;

        var isType = value.EndsWith('~');
        var parts = value[4..].Split('~');
        if (isType)
            parts = parts[..^1];
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
            return false;

        var hasUuidTail = !isType && parts.Length >= 2 && Guid.TryParseExact(parts[^1], "D", out _);
        var segmentCount = hasUuidTail ? parts.Length - 1 : parts.Length;
        var segments = new List<GtsIdSegment>(parts.Length);
        for (var index = 0; index < segmentCount; index++)
        {
            var result = Parsers.Segment.Parse(parts[index]);
            if (!result.Success || !HasCanonicalVersion(parts[index], result.Value.Version))
                return false;
            var segment = result.Value;
            segments.Add(new GtsIdSegment(
                segment.Vendor,
                segment.Package,
                segment.Namespace,
                segment.Type,
                segment.Version?.Major,
                segment.Version?.Minor,
                index < segmentCount - 1 || isType || hasUuidTail,
                false));
        }

        if (hasUuidTail)
            segments.Add(new GtsIdSegment(null, null, null, null, null, null, false, false));

        parsed = new ParsedGtsId(value, segments, isType, hasUuidTail);
        return true;
    }

    private static bool HasCanonicalVersion(string source, Parsers.VersionInfo? version)
    {
        if (version is null || version.Value.Major < 0 || version.Value.Minor is < 0)
            return false;
        var tokens = source.Split('.');
        if (tokens.Length is not (5 or 6) || tokens[4] != $"v{version.Value.Major}")
            return false;
        return tokens.Length == 5 || tokens[5] == version.Value.Minor?.ToString();
    }
}