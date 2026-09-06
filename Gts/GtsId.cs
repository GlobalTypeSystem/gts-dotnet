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
        if (id is null)
        {
            result = null;
            return ParseResult.ArgumentIsNull;
        }
        
        var (parseResult, isType) = id.EndsWith("~")
            ? (Parsers.GtsTypeId.Parse(id), true)
            : (Parsers.GtsInstanceId.Parse(id), false);

        if (!parseResult.Success)
        {
            // TODO: add errors to the result
            result = null;
            return new ParseResult();
        }

        var segments = parseResult.Value
            .Select(MapSegment);

        result = new GtsId(id, new List<GtsIdSegment>(segments))
        {
            IsType = isType,
            IsInstance = !isType,
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
            // TODO: add errors to the result
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

    /// <summary>
    /// Returns true if this identifier matches the given pattern.
    /// Pattern may contain at most one trailing wildcard (*); matching is segment-by-segment.
    /// </summary>
    public bool Matches(GtsId pattern)
    {
        if (pattern is null) return false;

        // TODO: counting is probably not needed
        if (!pattern.Id.Contains('*'))
            return MatchSegments(pattern.Segments, Segments, true);

        if (pattern.Id.Count(c => c == '*') > 1 || !pattern.Id.EndsWith('*'))
            return false;

        return MatchSegments(pattern.Segments, Segments, false);
    }

    /// <summary>
    /// Returns true if this identifier matches the given pattern string.
    /// Pattern may contain at most one trailing wildcard (*).
    /// </summary>
    public bool Matches(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        // TODO: throwing is probably more idiomatic
        if (!TryParsePattern(pattern, out var patternId)) return false;
        return Matches(patternId!);
    }

    private static bool MatchSegments(
        IReadOnlyCollection<GtsIdSegment> patternSegs, IReadOnlyCollection<GtsIdSegment> candidateSegs, bool exact)
    {
        if (exact && patternSegs.Count != candidateSegs.Count) return false;
        if (patternSegs.Count > candidateSegs.Count) return false;

        var patternList = patternSegs as IList<GtsIdSegment> ?? patternSegs.ToList();
        var candidateList = candidateSegs as IList<GtsIdSegment> ?? candidateSegs.ToList();

        for (var i = 0; i < patternList.Count; i++)
        {
            var pSeg = patternList[i];
            var cSeg = candidateList[i];

            if (pSeg.IsWildcard)
            {
                if (pSeg.Vendor is not null && pSeg.Vendor != cSeg.Vendor) return false;
                if (pSeg.Package is not null && pSeg.Package != cSeg.Package) return false;
                if (pSeg.Namespace is not null && pSeg.Namespace != cSeg.Namespace) return false;
                if (pSeg.Type is not null && pSeg.Type != cSeg.Type) return false;
                
                if (pSeg.VersionMajor.HasValue && pSeg.VersionMajor != cSeg.VersionMajor) return false;
                if (pSeg.VersionMinor.HasValue && (cSeg.VersionMinor is null || pSeg.VersionMinor != cSeg.VersionMinor)) return false;
                
                return true;
            }

            if (pSeg.Vendor != cSeg.Vendor) return false;
            if (pSeg.Package != cSeg.Package) return false;
            if (pSeg.Namespace != cSeg.Namespace) return false;
            if (pSeg.Type != cSeg.Type) return false;
            if (pSeg.VersionMajor != cSeg.VersionMajor) return false;
            if (pSeg.VersionMinor.HasValue && (cSeg.VersionMinor is null || pSeg.VersionMinor != cSeg.VersionMinor)) return false;
        }

        return true;
    }

    /// <summary>
    /// Generates a deterministic UUID v5 from this GTS identifier using the GTS namespace.
    /// </summary>
    public Guid ToGuid()
        => GuidUtils.Create(GuidUtils.GtsNamespace, Id);

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
