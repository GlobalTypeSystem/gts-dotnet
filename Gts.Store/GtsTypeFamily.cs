using System.Text.RegularExpressions;

namespace Gts.Store;

/// <summary>
/// Helpers for grouping GTS type identifiers that differ only in the <strong>minor</strong> component
/// of the <strong>last</strong> segment (same vendor, package, namespace, type, and major version).
/// </summary>
public static class GtsTypeFamily
{
    private static readonly Regex LastMinorSuffix = new(
        @"\.v(\d+)\.(\d+)~$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Strips the minor version from the last <c>.vMAJOR.MINOR~</c> suffix of a GTS type id.
    /// Chained ids only the final segment is affected (e.g. <c>...v1.0~...v2.3~</c> → <c>...v1.0~...v2~</c>).
    /// </summary>
    public static string StripLastMinorFromTypeId(string typeId)
    {
        if (string.IsNullOrEmpty(typeId))
            return typeId;
        return LastMinorSuffix.Replace(typeId, ".v$1~");
    }

    /// <summary>
    /// Returns a key shared by all type ids in the same minor-evolution family (last-segment minor ignored).
    /// </summary>
    public static string GetMinorEvolutionFamilyKey(GtsId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!id.IsType)
            throw new ArgumentException("Expected a type identifier (trailing ~).", nameof(id));
        return StripLastMinorFromTypeId(id.Id);
    }

    /// <summary>
    /// True if both ids are type ids with identical segment chains except the <strong>last segment's minor</strong>,
    /// which must be present on both sides (evolution <c>v1.0</c> → <c>v1.1</c>).
    /// </summary>
    public static bool AreSameLogicalTypeMinorVariants(GtsId a, GtsId b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!a.IsType || !b.IsType)
            return false;
        if (a.Segments.Count != b.Segments.Count)
            return false;

        var aSegs = a.Segments as IList<GtsIdSegment> ?? a.Segments.ToList();
        var bSegs = b.Segments as IList<GtsIdSegment> ?? b.Segments.ToList();

        for (var i = 0; i < aSegs.Count; i++)
        {
            var x = aSegs[i];
            var y = bSegs[i];
            var last = i == aSegs.Count - 1;
            if (!SegmentEqualsForMinorVariantPair(x, y, last))
                return false;
        }

        return true;
    }

    private static bool SegmentEqualsForMinorVariantPair(GtsIdSegment x, GtsIdSegment y, bool isLast)
    {
        if (x.Vendor != y.Vendor) return false;
        if (x.Package != y.Package) return false;
        if (x.Namespace != y.Namespace) return false;
        if (x.Type != y.Type) return false;
        if (x.VersionMajor != y.VersionMajor) return false;

        if (!isLast)
            return x.VersionMinor == y.VersionMinor;

        // Last segment: both must carry an explicit minor (MINOR evolution); values may match (identity).
        return x.VersionMinor is not null
            && y.VersionMinor is not null;
    }
}
