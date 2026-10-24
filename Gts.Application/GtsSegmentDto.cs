using System.Text.Json.Nodes;
using Gts;

namespace Gts.Application;

/// <summary>Maps a §10 <see cref="GtsPattern.SegmentView"/> to JSON-friendly segment objects (snake_case fields).</summary>
public static class GtsSegmentDto
{
    /// <summary>
    /// Maps a §10 <see cref="GtsPattern.SegmentView"/> to a JSON segment object. A <see cref="JsonObject"/>
    /// is used so <c>ver_minor</c> is always present (as explicit <c>null</c> when a segment omits the
    /// minor), rather than dropped by the server's global "ignore null" serialization policy.
    /// </summary>
    public static object FromSegment(GtsPattern.SegmentView s) => new JsonObject
    {
        ["vendor"] = s.Vendor,
        ["package"] = s.Package,
        ["namespace"] = s.Namespace,
        ["type"] = s.Type,
        ["ver_major"] = s.VersionMajor is { } major ? JsonValue.Create(major) : null,
        ["ver_minor"] = s.VersionMinor is { } minor ? JsonValue.Create(minor) : null,
        ["is_type"] = s.IsType
    };
}
