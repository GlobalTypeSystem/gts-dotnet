using System.Globalization;

namespace Gts;

/// <summary>
/// A GTS identifier match pattern (README §10): a validated GTS identifier that may end in a single
/// trailing <c>*</c> wildcard, but may also be fully concrete. A zero-wildcard pattern is not an
/// exact match — a base Type ID used as a pattern also matches everything derived from or
/// instantiated under it, with minor-version flexibility (so <c>…type.v1~</c> behaves as the
/// implicit envelope <c>…type.v1~*</c>).
///
/// This is a direct port of the Rust reference (<c>gts-id/src/parse.rs</c>, <c>gts_id_segment.rs</c>,
/// <c>gts_id_pattern.rs</c>). It supersedes the legacy regex-based matching for GTS reference and
/// pattern keywords, implementing identifier matching (<see cref="Matches(GtsId)"/>) and pattern
/// coverage (<see cref="Covers"/>), including the unsigned 32-bit version bounds and the
/// <c>v1.*</c> / <c>v1~*</c> / <c>v*</c> distinctions.
/// </summary>
public sealed class GtsPattern
{
    /// <summary>Maximum allowed length of a GTS identifier or pattern string.</summary>
    public const int MaxLength = 1024;

    private const string Prefix = "gts.";

    /// <summary>Four one-letter names, four dots and <c>v0</c>: the shortest instance segment, <c>a.a.a.a.v0</c>.</summary>
    private const int MinSegmentLen = 10;

    private readonly string _pattern;
    private readonly IReadOnlyList<Segment> _segments;

    private GtsPattern(string pattern, IReadOnlyList<Segment> segments)
    {
        _pattern = pattern;
        _segments = segments;
    }

    /// <summary>The validated pattern string.</summary>
    public string Pattern => _pattern;

    /// <summary>Whether this pattern ends in a <c>*</c> wildcard (otherwise it is a concrete identifier).</summary>
    public bool HasWildcard => _pattern.Contains('*');

    /// <summary>A read-only view of one parsed segment (unspecified name tokens are <c>null</c>).</summary>
    public readonly record struct SegmentView(
        string? Vendor, string? Package, string? Namespace, string? Type,
        long? VersionMajor, long? VersionMinor, bool IsType, bool IsWildcard);

    /// <summary>The parsed segments, for inspection (OP#1/OP#3 parse output).</summary>
    public IReadOnlyList<SegmentView> Segments => _segments
        .Select(s => new SegmentView(
            s.Vendor.Length == 0 ? null : s.Vendor,
            s.Package.Length == 0 ? null : s.Package,
            s.Namespace.Length == 0 ? null : s.Namespace,
            s.TypeName.Length == 0 ? null : s.TypeName,
            s.VerMajor, s.VerMinor, s.IsType, s.IsWildcard))
        .ToList();

    /// <summary>Parses a §10 pattern (concrete identifier or trailing <c>*</c> wildcard).</summary>
    public static bool TryParse(string? text, out GtsPattern? pattern)
    {
        pattern = null;
        if (text is null)
            return false;
        if (!TryParseSegments(text, allowWildcards: true, out var segments))
            return false;
        pattern = new GtsPattern(text, segments);
        return true;
    }

    /// <summary>Whether <paramref name="text"/> is a valid §10 pattern.</summary>
    public static bool IsValid(string? text) => TryParse(text, out _);

    /// <summary>Whether <paramref name="id"/> (a concrete identifier) is selected by this pattern (<c>id ∈ M(this)</c>).</summary>
    public bool Matches(GtsId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return TryParseSegments(id.Id, allowWildcards: false, out var candidate) && MatchesSegments(candidate);
    }

    /// <summary>Whether the concrete identifier string <paramref name="candidate"/> is selected by this pattern.</summary>
    public bool MatchesCandidate(string candidate) =>
        TryParseSegments(candidate, allowWildcards: false, out var segments) && MatchesSegments(segments);

    /// <summary>Whether this pattern covers <paramref name="other"/>: <c>M(other) ⊆ M(this)</c>.</summary>
    public bool Covers(GtsPattern other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var value = Selection.Of(other);
        var shortest = value.ShortestSelected();
        // A valid pattern can select no canonical identifier (every selection would exceed the
        // length limit); then M(other) is empty and every pattern covers it.
        return shortest > MaxLength || CoversNonempty(Selection.Of(this), value, shortest == MaxLength);
    }

    // ---- matching (port of GtsIdPattern::matches_views) ------------------------------------

    private bool MatchesSegments(IReadOnlyList<Segment> candidate)
    {
        var pattern = _segments;
        // A final bare `~*` may match an empty chain suffix. A wildcard that already specifies part
        // of the next segment (for example `~abc.*`) still requires that segment to exist.
        var matchesEmptySuffix = pattern.Count > 0 && pattern[^1].IsWildcard && pattern[^1].Raw == "*";
        var requiredLen = pattern.Count - (matchesEmptySuffix ? 1 : 0);
        if (requiredLen > candidate.Count)
            return false;

        for (var i = 0; i < pattern.Count; i++)
        {
            if (i == candidate.Count)
                return matchesEmptySuffix && i == pattern.Count - 1;
            var p = pattern[i];
            var c = candidate[i];

            if (p.IsWildcard)
            {
                if (p.Vendor.Length != 0 && p.Vendor != c.Vendor) return false;
                if (p.Package.Length != 0 && p.Package != c.Package) return false;
                if (p.Namespace.Length != 0 && p.Namespace != c.Namespace) return false;
                if (p.TypeName.Length != 0 && p.TypeName != c.TypeName) return false;
                if (p.VerMajor is { } pm && c.VerMajor != pm) return false;
                // `v1.*` requires an explicit minor in the concrete id.
                if (p.VerMajor is not null && c.VerMinor is null) return false;
                if (p.VerMinor is { } pmin && c.VerMinor != pmin) return false;
                return true;
            }

            // Non-wildcard UUID tail — compare the raw UUID string.
            if (p.UuidTail is not null && p.UuidTail != c.UuidTail) return false;
            if (p.Vendor != c.Vendor) return false;
            if (p.Package != c.Package) return false;
            if (p.Namespace != c.Namespace) return false;
            if (p.TypeName != c.TypeName) return false;
            if (p.VerMajor != c.VerMajor) return false;
            if (p.VerMinor is { } min && c.VerMinor != min) return false;
            if (p.IsType != c.IsType) return false;
        }
        return true;
    }

    // ---- coverage (port of GtsIdPattern::covers) -------------------------------------------

    private abstract record Selection
    {
        internal sealed record Instance(IReadOnlyList<Segment> Types, Segment Tail, int Length) : Selection;

        internal sealed record Prefixed(IReadOnlyList<Segment> Types, Segment? Next) : Selection;

        internal static Selection Of(GtsPattern pattern)
        {
            var segments = pattern._segments;
            var end = segments.Count - 1;
            var last = segments[end];
            if (last.IsWildcard)
                return new Prefixed(Slice(segments, 0, end), last.Raw != "*" ? last : null);
            if (last.IsType)
                return new Prefixed(segments, null);
            return new Instance(Slice(segments, 0, end), last, pattern._pattern.Length);
        }

        internal int ShortestSelected()
        {
            switch (this)
            {
                case Instance instance:
                    return instance.Length;
                case Prefixed prefixed:
                    var chain = Prefix.Length + prefixed.Types.Sum(s => s.Raw.Length);
                    if (prefixed.Next is null && prefixed.Types.Count != 0)
                        return chain;
                    var segment = prefixed.Next is { } next ? ShortestPartial(next) : MinSegmentLen;
                    // A first segment must end in the type marker.
                    return chain + segment + (prefixed.Types.Count == 0 ? 1 : 0);
                default:
                    return 0;
            }
        }
    }

    private static int ShortestPartial(Segment partial)
    {
        if (partial.VerMajor is not null)
            // In `v1.*`, replacing `*` with the shortest minor (`0`) keeps the length.
            return partial.Raw.Length;
        // Missing names are one character; `.v0` and the name separators add six.
        return Math.Max(partial.Vendor.Length, 1) + Math.Max(partial.Package.Length, 1)
            + Math.Max(partial.Namespace.Length, 1) + Math.Max(partial.TypeName.Length, 1) + 6;
    }

    private static bool CoversNonempty(Selection operand, Selection value, bool atLimit)
    {
        switch (operand, value)
        {
            case (Selection.Instance a, Selection.Instance b):
                if (!SameChainCovers(a.Types, b.Types))
                    return false;
                return (a.Tail.UuidTail, b.Tail.UuidTail) switch
                {
                    (not null, not null) => a.Tail.UuidTail == b.Tail.UuidTail,
                    (null, null) => SegmentCovers(a.Tail, b.Tail),
                    _ => false,
                };

            case (Selection.Instance a, Selection.Prefixed { Next: { } partial } b) when atLimit:
                // Only one-digit instance minors fit; appending a type marker or another chain
                // segment would exceed the length limit.
                return SameChainCovers(a.Types, b.Types)
                    && a.Tail.UuidTail is null
                    && a.Tail.VerMinor is null
                    && SameNames(a.Tail, partial)
                    && partial.VerMajor == a.Tail.VerMajor;

            case (Selection.Instance, Selection.Prefixed):
                return false;

            case (Selection.Prefixed p, Selection.Instance inst):
                return inst.Types.Count >= p.Types.Count
                    && p.Types.Where((t, i) => SegmentCovers(t, inst.Types[i])).Count() == p.Types.Count
                    && (p.Next is null || PartialAdmits(p.Next, p.Types.Count < inst.Types.Count ? inst.Types[p.Types.Count] : inst.Tail));

            case (Selection.Prefixed a, Selection.Prefixed b):
                return PrefixedCovers(a.Types, a.Next, b.Types, b.Next);

            default:
                return false;
        }
    }

    private static bool SameNames(Segment a, Segment b) =>
        a.Vendor == b.Vendor && a.Package == b.Package && a.Namespace == b.Namespace && a.TypeName == b.TypeName;

    private static bool SegmentCovers(Segment a, Segment b) =>
        SameNames(a, b) && a.VerMajor == b.VerMajor && (a.VerMinor is null || a.VerMinor == b.VerMinor);

    private static bool SameChainCovers(IReadOnlyList<Segment> a, IReadOnlyList<Segment> b) =>
        a.Count == b.Count && !a.Where((t, i) => !SegmentCovers(t, b[i])).Any();

    private static bool PartialNamesCover(Segment a, Segment b) =>
        (a.Vendor.Length == 0 || a.Vendor == b.Vendor)
        && (a.Package.Length == 0 || a.Package == b.Package)
        && (a.Namespace.Length == 0 || a.Namespace == b.Namespace)
        && (a.TypeName.Length == 0 || a.TypeName == b.TypeName);

    private static bool PartialAdmits(Segment partial, Segment segment) =>
        PartialNamesCover(partial, segment)
        && (partial.VerMajor is not { } major || (segment.VerMajor == major && segment.VerMinor is not null));

    private static bool PartialCovers(Segment a, Segment b) =>
        PartialNamesCover(a, b) && (a.VerMajor is not { } major || b.VerMajor == major);

    private static bool PrefixedCovers(IReadOnlyList<Segment> a, Segment? aNext, IReadOnlyList<Segment> b, Segment? bNext)
    {
        if (a.Count > b.Count)
        {
            // A first-segment minor wildcard selects types contained in a bare type scope with the
            // same names and major and an omitted minor.
            if (a.Count == 1 && aNext is null && b.Count == 0 && bNext is { } partial)
                return a[0].VerMinor is null && SameNames(a[0], partial) && a[0].VerMajor == partial.VerMajor;
            return false;
        }
        for (var i = 0; i < a.Count; i++)
            if (!SegmentCovers(a[i], b[i]))
                return false;
        if (aNext is not { } next)
            return true;
        if (a.Count < b.Count)
            return PartialAdmits(next, b[a.Count]);
        return bNext is { } other && PartialCovers(next, other);
    }

    private static IReadOnlyList<Segment> Slice(IReadOnlyList<Segment> segments, int start, int end)
    {
        var list = new List<Segment>(end - start);
        for (var i = start; i < end; i++)
            list.Add(segments[i]);
        return list;
    }

    // ---- parsing (port of parse.rs + gts_id_segment.rs) ------------------------------------

    private sealed record Segment(
        string Raw, string Vendor, string Package, string Namespace, string TypeName,
        long? VerMajor, long? VerMinor, bool IsWildcard)
    {
        public bool IsType => Raw.EndsWith('~');
        public string? UuidTail => IsUuid(Raw) ? Raw : null;
    }

    private static bool TryParseSegments(string id, bool allowWildcards, out IReadOnlyList<Segment> segments)
    {
        segments = Array.Empty<Segment>();
        if (!id.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (id != id.ToLowerInvariant() || id.Length > MaxLength)
            return false;

        if (allowWildcards)
        {
            var stars = id.Count(c => c == '*');
            if (stars > 1 || (stars == 1 && !id.EndsWith('*')))
                return false;
        }

        var remainder = id[Prefix.Length..];
        var tildeParts = remainder.Split('~');

        var last = tildeParts.Length > 0 ? tildeParts[^1] : "";
        var uuidTail = IsUuid(last) && tildeParts.Length >= 2 ? last : null;

        var segmentsPortion = uuidTail is not null ? id[..(id.Length - uuidTail.Length - 1)] : id;
        if (segmentsPortion.Contains('-'))
            return false;

        var count = tildeParts.Length - (uuidTail is not null ? 1 : 0);
        var raws = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var part = tildeParts[i];
            var isLast = i == count - 1;
            if (part.Length == 0)
            {
                if (!(isLast && uuidTail is null))
                    return false;
            }
            else if (isLast && uuidTail is null)
                raws.Add(part);
            else
                raws.Add(part + "~");
        }
        if (raws.Count == 0)
            return false;

        var result = new List<Segment>(raws.Count + 1);
        foreach (var raw in raws)
        {
            if (!TryParseSegment(raw, allowWildcards, out var segment))
                return false;
            result.Add(segment);
        }
        if (uuidTail is not null)
            result.Add(new Segment(uuidTail, "", "", "", "", null, null, false));

        // Issue #37: a single-segment instance id is prohibited (wildcard/UUID-tail exempt).
        if (uuidTail is null && result.Count == 1 && !result[0].IsType && !result[0].IsWildcard)
            return false;

        segments = result;
        return true;
    }

    private static bool TryParseSegment(string raw, bool allowWildcards, out Segment segment)
    {
        segment = null!;
        var seg = raw;
        if (seg.Contains('~'))
        {
            if (seg.Count(c => c == '~') > 1 || !seg.EndsWith('~'))
                return false;
            seg = seg[..^1];
        }

        var tokens = seg.Split('.');
        if (tokens.Length > 6)
            return false;

        var endsWithWildcard = allowWildcards && seg.EndsWith('*');
        if (!endsWithWildcard && tokens.Length < 5)
            return false;

        // Reject an extra name token before the version, e.g. vendor.package.namespace.type.extra.v1.
        if (!endsWithWildcard && tokens.Length == 6)
        {
            var hasWildcard = allowWildcards && tokens.Contains("*");
            if (!hasWildcard && !tokens[4].StartsWith('v') && tokens[5].StartsWith('v') && IsValidToken(tokens[4]))
                return false;
        }

        for (var i = 0; i < Math.Min(4, tokens.Length); i++)
        {
            if (allowWildcards && tokens[i] == "*")
            {
                if (i == tokens.Length - 1)
                    break;
                return false;
            }
            if (!IsValidToken(tokens[i]))
                return false;
        }

        string vendor = "", package = "", ns = "", type = "";
        long? major = null, minor = null;

        if (tokens.Length > 0)
        {
            if (allowWildcards && tokens[0] == "*") { segment = Wildcard(raw, vendor, package, ns, type, major, minor); return true; }
            vendor = tokens[0];
        }
        if (tokens.Length > 1)
        {
            if (allowWildcards && tokens[1] == "*") { segment = Wildcard(raw, vendor, package, ns, type, major, minor); return true; }
            package = tokens[1];
        }
        if (tokens.Length > 2)
        {
            if (allowWildcards && tokens[2] == "*") { segment = Wildcard(raw, vendor, package, ns, type, major, minor); return true; }
            ns = tokens[2];
        }
        if (tokens.Length > 3)
        {
            if (allowWildcards && tokens[3] == "*") { segment = Wildcard(raw, vendor, package, ns, type, major, minor); return true; }
            type = tokens[3];
        }
        if (tokens.Length > 4)
        {
            if (allowWildcards && tokens[4] == "*")
            {
                if (4 != tokens.Length - 1) return false;
                segment = Wildcard(raw, vendor, package, ns, type, major, minor);
                return true;
            }
            // Glued version wildcard `v*` means "any version".
            if (allowWildcards && tokens[4] == "v*")
            {
                if (4 != tokens.Length - 1) return false;
                segment = Wildcard(raw, vendor, package, ns, type, major, minor);
                return true;
            }
            if (!tokens[4].StartsWith('v'))
                return false;
            if (!TryParseU32Exact(tokens[4][1..], out var m))
                return false;
            major = m;
        }
        if (tokens.Length > 5)
        {
            if (allowWildcards && tokens[5] == "*")
            {
                segment = Wildcard(raw, vendor, package, ns, type, major, minor);
                return true;
            }
            if (!TryParseU32Exact(tokens[5], out var mn))
                return false;
            minor = mn;
        }

        segment = new Segment(raw, vendor, package, ns, type, major, minor, false);
        return true;
    }

    private static Segment Wildcard(string raw, string vendor, string package, string ns, string type, long? major, long? minor) =>
        new(raw, vendor, package, ns, type, major, minor, true);

    private static bool IsValidToken(string token)
    {
        if (token.Length == 0)
            return false;
        var first = token[0];
        if (first != '_' && first is < 'a' or > 'z')
            return false;
        foreach (var c in token)
            if (c != '_' && c is < 'a' or > 'z' && c is < '0' or > '9')
                return false;
        return true;
    }

    private static bool TryParseU32Exact(string value, out long parsed)
    {
        parsed = 0;
        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var u))
            return false;
        if (u.ToString(CultureInfo.InvariantCulture) != value)
            return false;
        parsed = u;
        return true;
    }

    private static bool IsUuid(string s)
    {
        if (s.Length != 36)
            return false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i is 8 or 13 or 18 or 23)
            {
                if (c != '-')
                    return false;
            }
            else if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}
