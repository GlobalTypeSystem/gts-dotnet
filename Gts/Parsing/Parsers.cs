using System.Collections;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace Gts.Parsing;

/// <summary>Internal parser definitions for GTS type, instance, and pattern identifiers.</summary>
internal static class Parsers
{
    internal static readonly Parser<char, char> Dot =
        Char('.').Labelled(".");
    internal static readonly Parser<char, char> Tilde =
        Char('~').Labelled("~");

    internal static readonly Parser<char, string> Wildcard =
        String("*").Labelled(nameof(Wildcard));
    
    internal static readonly Parser<char, char> V =
        Char('v').Labelled(nameof(V));

    internal static readonly Parser<char, int> VersionNumber =
        Num.Labelled(nameof(VersionNumber));
    internal static readonly Parser<char, int> VersionMajor =
        V.Then(VersionNumber).Labelled(nameof(VersionMajor));
    internal static readonly Parser<char, int> VersionMinor =
        VersionNumber.Labelled(nameof(VersionMinor));

    internal static readonly Parser<char, VersionInfo> VersionFull =
        VersionMajor.Then(
            Dot.Then(VersionMinor).Optional(),
            (major, minor) => new VersionInfo(major, minor.HasValue ? minor.Value : null))
            .Labelled(nameof(VersionFull));

    internal static readonly Parser<char, string> VersionFullString =
        VersionFull.Select(v => v.ToString())
            .Labelled(nameof(VersionFullString));

    // internal static readonly Parser<char, VersionInfo?> VersionSuffix =
    //     Dot.Then(VersionFull)
    //     .Optional().Select(v => v.HasValue ? (VersionInfo?)v.Value : null);

    private static readonly Parser<char, char> IdentifierStart =
        Token(c => c == '_' || c is >= 'a' and <= 'z');

    private static readonly Parser<char, char> IdentifierRest =
        Token(c => c == '_' || c is >= 'a' and <= 'z' || c is >= '0' and <= '9');

    internal static readonly Parser<char, string> Identifier =
        IdentifierStart.Then(IdentifierRest.Many(), (first, rest) => first + string.Concat(rest))
            .Labelled(nameof(Identifier));

    internal static readonly Parser<char, string> IdentifierOrWildcard =
        Wildcard.Or(Identifier)
            .Labelled(nameof(Identifier));
    
    internal static readonly Parser<char, string> GtsPrefix =
        String("gts").Labelled(nameof(GtsPrefix));
    
    internal static readonly Parser<char, string> Vendor =
        Identifier.Labelled(nameof(Vendor));
    internal static readonly Parser<char, string> Package =
        Identifier.Labelled(nameof(Package));
    internal static readonly Parser<char, string> Namespace =
        Identifier.Labelled(nameof(Namespace));
    internal static readonly Parser<char, string> Type =
        Identifier.Labelled(nameof(Type));
    
    internal static readonly Parser<char, SegmentInfo> Segment =
        Vendor.Before(Dot)
            .Then(Package.Before(Dot), (vendor, package) => (vendor, package))
            .Then(Namespace.Before(Dot), (t, nspace) => (t.vendor, t.package, nspace))
            .Then(Type.Before(Dot), (t, type) => (t.vendor, t.package, t.nspace, type))
            .Then(VersionFull,
                (t, version) => new SegmentInfo(
                    t.vendor,
                    t.package,
                    t.nspace,
                    t.type,
                    version,
                    false))
            .Labelled(nameof(Segment));
    
    internal static readonly Parser<char, SegmentInfo> Pattern =
        Vendor.Before(Dot).Optional()
            .Then(Package.Before(Dot).Optional(), (vendor, package) => (vendor, package))
            .Then(Namespace.Before(Dot).Optional(), (t, nspace) => (t.vendor, t.package, nspace))
            .Then(Type.Before(Dot).Optional(), (t, type) => (t.vendor, t.package, t.nspace, type))
            .Then(Wildcard.Or(VersionFullString),
                (t, version) => new SegmentInfo(
                    t.vendor.GetValueOrDefault(),
                    t.package.GetValueOrDefault(),
                    t.nspace.GetValueOrDefault(),
                    t.type.GetValueOrDefault(),
                    version is "*"
                        ? null
                        : VersionInfo.FromString(version),
                    version is "*"))
            .Labelled(nameof(Pattern));

    // internal static readonly Parser<char, TypeInfo> TypeSuffix =
    //     Try(Tilde.Then(Segment)).AtLeastOnce().Select(segments => new TypeInfo(segments))
    //         .Or(Tilde.Select(_ => new TypeInfo([])));

    // internal static readonly Parser<char, InstanceInfo> InstanceSuffix =
    //     Tilde.Then(Segment)
    //         .Then(Tilde.Then(Segment).Many(), (head, tail) => new InstanceInfo(head, tail));

    internal static readonly Parser<char, IdentifierInfo> GtsTypeId =
        GtsPrefix.Then(Dot)
            .Then(Segment.SeparatedAndTerminatedAtLeastOnce(Tilde),
                (_, segments) => new IdentifierInfo(IdentifierKind.Type, segments))
            .Before(End);

    internal static readonly Parser<char, IdentifierInfo> GtsInstanceId =
        GtsPrefix.Then(Dot)
            .Then(Segment.SeparatedAtLeastOnce(Tilde),
                (_, segments) => new IdentifierInfo(IdentifierKind.Instance, segments))
            .Before(End);
    
    internal static readonly Parser<char, IdentifierInfo> GtsPattern =
        GtsPrefix.Then(Dot)
            .Then(Pattern.SeparatedAndOptionallyTerminatedAtLeastOnce(Tilde),
                (_, segments) => new IdentifierInfo(IdentifierKind.Pattern, segments))
            .Before(End);

    /// <summary>Major and optional minor version from a GTS segment.</summary>
    internal record struct VersionInfo(
        int Major,
        int? Minor)
    {
        /// <inheritdoc/>
        public override string ToString() => Minor == null
            ? $"v{Major}"
            : $"v{Major}.{Minor}";

        /// <summary>Parses a version string (e.g. "v1" or "v1.0").</summary>
        public static VersionInfo? FromString(string str)
        {
            var parts = str.Substring(1).Split('.');
            return parts.Length == 1
                ? new VersionInfo(int.Parse(parts[0]), null)
                : new VersionInfo(int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }

    /// <summary>Parsed segment: vendor.package.namespace.type.version or wildcard.</summary>
    internal record struct SegmentInfo(
        string? Vendor,
        string? Package,
        string? Namespace,
        string? Type,
        VersionInfo? Version,
        bool IsWildcard);

    /// <summary>Kind of GTS identifier (type, instance, or pattern).</summary>
    internal enum IdentifierKind
    {
        Type,
        Instance,
        Pattern,
    }
    
    /// <summary>Parsed identifier with kind and segment list.</summary>
    internal record struct IdentifierInfo(
        IdentifierKind Kind, IEnumerable<SegmentInfo> Segments) : IEnumerable<SegmentInfo>
    {
        /// <summary>True if this is a type ID.</summary>
        public bool IsType => Kind == IdentifierKind.Type;
        /// <summary>True if this is an instance ID.</summary>
        public bool IsInstance => Kind == IdentifierKind.Instance;
        /// <summary>True if this is a pattern.</summary>
        public bool IsPattern => Kind == IdentifierKind.Pattern;
        
        /// <inheritdoc/>
        public IEnumerator<SegmentInfo> GetEnumerator() => Segments.GetEnumerator();
        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
