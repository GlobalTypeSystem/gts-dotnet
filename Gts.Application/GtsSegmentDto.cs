using Gts;

namespace Gts.Application;

/// <summary>Maps <see cref="GtsIdSegment"/> to JSON-friendly segment objects (snake_case fields).</summary>
public static class GtsSegmentDto
{
    public static object FromSegment(GtsIdSegment s) => new
    {
        vendor = s.Vendor,
        package = s.Package,
        @namespace = s.Namespace,
        type = s.Type,
        ver_major = s.VersionMajor,
        ver_minor = s.VersionMinor,
        is_type = s.IsType
    };
}
