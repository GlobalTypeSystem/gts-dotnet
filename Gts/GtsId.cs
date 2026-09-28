using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Gts.Parsing;
using Gts.Utils;
using Pidgin;
using ParseException = Gts.Parsing.ParseException;

namespace Gts;

/// <summary>
/// A validated GTS identifier.
/// </summary>
public sealed class GtsId
{
    /// <summary>Maximum allowed length of a GTS identifier string.</summary>
    public const int MaxLength = 1024;

    /// <summary>
    /// The canonical identifier string (lowercase, trimmed).
    /// </summary>
    public string Id { get; }

    /// <summary>True if this ID is a type identifier (ends with ~).</summary>
    public bool IsType { get; private set; }

    /// <summary>True if this ID is an instance identifier (does not end with ~).</summary>
    public bool IsInstance { get; private set; }

    /// <summary>True if this ID was parsed as a pattern (may contain wildcards).</summary>
    public bool IsPattern { get; private set; }

    /// <summary>
    /// Parsed segments (vendor.package.namespace.type.version per segment).
    /// </summary>
    public IReadOnlyCollection<GtsIdSegment> Segments { get; }

    /// <summary>Creates a GTS ID from a canonical string and parsed segments.</summary>
    internal GtsId(string id, IReadOnlyCollection<GtsIdSegment> segments)
    {
        Id = id;
        Segments = segments;
    }

    /// <summary>Parses a GTS type or instance ID; throws <see cref="ParseException"/> on failure.</summary>
    public static GtsId Parse(string id)
    {
        var parseResult = TryParseInternal(id, out GtsId? result);

        if (parseResult)
        {
            return result!;
        }

        throw new ParseException(parseResult);
    }

    /// <summary>Attempts to parse a GTS type or instance ID without throwing.</summary>
    public static ParseResult TryParse(string? id, out GtsId? result)
    {
        return TryParseInternal(id, out result);
    }

    /// <summary>Parses a GTS pattern ID; throws <see cref="ParseException"/> on failure.</summary>
    public static GtsId ParsePattern(string pattern)
    {
        var parseResult = TryParsePatternInternal(pattern, out GtsId? result);

        if (parseResult)
        {
            return result!;
        }

        throw new ParseException(parseResult);
    }

    /// <summary>Attempts to parse a GTS pattern ID without throwing.</summary>
    public static ParseResult TryParsePattern(string? pattern, out GtsId? result)
    {
        return TryParsePatternInternal(pattern, out result);
    }

    private static ParseResult TryParseInternal(string? id, out GtsId? result)
    {
        result = null;
        if (id is null)
            return ParseResult.ArgumentIsNull;
        if (!GtsIdParser.TryParse(id, MaxLength, out var parsed) || parsed is null)
            return new ParseResult();

        result = new GtsId(parsed.CanonicalId, parsed.Segments)
        {
            IsType = parsed.IsType,
            IsInstance = !parsed.IsType,
            IsPattern = false
        };
        return ParseResult.Success;
    }

    private static ParseResult TryParsePatternInternal(string? pattern, out GtsId? result)
    {
        if (pattern is null)
        {
            result = null;
            return ParseResult.ArgumentIsNull;
        }

        var parseResult = Parsers.GtsPattern.Parse(pattern);

        if (!parseResult.Success)
        {
            result = null;
            return new ParseResult();
        }

        var segments = parseResult.Value
            .Select(MapSegment);

        result = new GtsId(pattern, new List<GtsIdSegment>(segments))
        {
            IsPattern = true
        };

        return ParseResult.Success;
    }

    private static GtsIdSegment MapSegment(Parsers.SegmentInfo s)
    {
        return new GtsIdSegment(
            s.Vendor, s.Package, s.Namespace, s.Type, s.Version?.Major, s.Version?.Minor, true, s.IsWildcard);
    }

    public static bool TryMatch(string candidate, string pattern, out bool match)
    {
        match = false;
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(pattern))
            return false;
        if (candidate.Contains('*') && (candidate.Count(character => character == '*') != 1 || !candidate.EndsWith('*') || !TryParsePattern(candidate, out _)))
            return false;
        if (!candidate.Contains('*') && !TryParse(candidate, out _))
            return false;
        if (pattern.Contains('*') && (pattern.Count(character => character == '*') != 1 || !pattern.EndsWith('*') || pattern.Length < 2 || pattern[^2] is not ('.' or '~')))
            return false;
        if (!pattern.Contains('*') && !TryParse(pattern, out _))
            return false;

        match = PatternRegex(pattern).IsMatch(candidate);
        return true;
    }

    /// <summary>
    /// Returns true if this identifier matches the given pattern.
    /// Pattern may contain at most one trailing wildcard (*).
    /// </summary>
    public bool Matches(GtsId pattern) => pattern is not null && Matches(pattern.Id);

    /// <summary>
    /// Returns true if this identifier matches the given pattern string.
    /// Pattern may contain at most one trailing wildcard (*).
    /// </summary>
    public bool Matches(string pattern)
    {
        // A `~*` pattern matches descendants of the type, not the bare type identifier itself.
        if (pattern.EndsWith("~*", StringComparison.Ordinal) && Id == pattern[..^1])
            return false;
        return TryMatch(Id, pattern, out var match) && match;
    }

    // Compiled-pattern cache. All GTS matching flows (Matches, TryMatch, and the CLI/HTTP match
    // operations) resolve through a single regex translation, so behavior cannot drift between two
    // implementations, and the (previously per-call) regex compilation is amortized across calls.
    private static readonly ConcurrentDictionary<string, Regex> PatternRegexCache = new(StringComparer.Ordinal);

    private static Regex PatternRegex(string pattern)
    {
        // Bound the cache: patterns can come from untrusted input, so cap growth (see GtsJsonSchemaEngine).
        if (PatternRegexCache.Count >= 1024)
            PatternRegexCache.Clear();

        return PatternRegexCache.GetOrAdd(pattern, static p =>
        {
            var expression = Regex.Escape(p);
            expression = Regex.Replace(expression, @"v([0-9]+)~", "v$1(?:\\.[0-9]+)?~");
            expression = Regex.Replace(expression, @"v([0-9]+)$", "v$1(?:\\.[0-9]+)?");
            var wildcard = p.EndsWith('*');
            if (wildcard) expression = expression[..^2] + ".*";
            return new Regex(
                "^" + expression + (wildcard || p.EndsWith('~') ? "" : "$"),
                RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });
    }

    /// <summary>
    /// Generates a deterministic UUID v5 from this GTS identifier using the GTS namespace.
    /// </summary>
    public Guid ToGuid()
    {
        var tail = Id[(Id.LastIndexOf('~') + 1)..];
        return Guid.TryParseExact(tail, "D", out var embedded)
            ? embedded
            : GuidUtils.Create(GuidUtils.GtsNamespace, Id);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is GtsId id && string.Equals(Id, id.Id, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Id);

    /// <summary>
    /// Returns the canonical identifier string (same as <see cref="Id"/>).
    /// </summary>
    public override string ToString() => Id;
}
