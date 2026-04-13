using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Resolves <c>@path</c> selectors against JSON content (OP#11).</summary>
public static class GtsAttributeSelector
{
    /// <summary>Splits <paramref name="gtsWithPath"/> into GTS id and JSON path after <c>@</c>.</summary>
    public static (string? GtsId, string? JsonPath) SplitGtsWithPath(string gtsWithPath)
    {
        var at = gtsWithPath.IndexOf('@');
        if (at < 0)
            return (gtsWithPath, null);
        return (gtsWithPath[..at], gtsWithPath[(at + 1)..]);
    }

    /// <summary>Walks <paramref name="root"/> following a dotted path with optional <c>[index]</c> segments.</summary>
    public static bool TryResolve(JsonNode? root, string jsonPath, out JsonNode? value)
    {
        value = null;
        if (root is null || string.IsNullOrEmpty(jsonPath))
            return false;

        JsonNode? cur = root;
        foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryStep(cur, segment.AsSpan(), out cur))
                return false;
        }

        value = cur;
        return true;
    }

    private static bool TryStep(JsonNode? cur, ReadOnlySpan<char> segment, out JsonNode? next)
    {
        next = null;
        if (cur is null)
            return false;

        var bracket = segment.IndexOf('[');
        if (bracket < 0)
        {
            var name = segment.ToString();
            if (cur is not JsonObject obj || !obj.TryGetPropertyValue(name, out next))
                return false;
            return true;
        }

        var prop = segment[..bracket].ToString();
        var close = segment.IndexOf(']');
        if (close < bracket)
            return false;
        var idxStr = segment[(bracket + 1)..close];
        if (!int.TryParse(idxStr, out var index))
            return false;

        if (cur is not JsonObject o || !o.TryGetPropertyValue(prop, out var arrNode) || arrNode is not JsonArray arr)
            return false;
        if (index < 0 || index >= arr.Count)
            return false;

        next = arr[index];
        if (close + 1 < segment.Length && segment[close + 1] == '.')
        {
            // More after ] — shouldn't happen if we split by '.' correctly
        }

        return true;
    }
}
