using System.Text;
using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Resolves <c>@path</c> selectors against JSON content (OP#11). Path rules align with gts-go <c>parsePath</c> / <c>resolveAttributePath</c>.</summary>
public static class GtsAttributeSelector
{
    /// <summary>Splits <paramref name="gtsWithPath"/> at the first <c>@</c> into instance id and attribute path (both trimmed).</summary>
    public static (string? GtsId, string? JsonPath) SplitGtsWithPath(string gtsWithPath)
    {
        var at = gtsWithPath.IndexOf('@');
        if (at < 0)
            return (gtsWithPath.Trim(), null);
        return (gtsWithPath[..at].Trim(), gtsWithPath[(at + 1)..].Trim());
    }

    /// <summary>Walks <paramref name="root"/> following a dotted path; <c>/</c> is treated like <c>.</c>; supports <c>prop[n]</c> and chained <c>prop[0][1]</c>.</summary>
    public static bool TryResolve(JsonNode? root, string jsonPath, out JsonNode? value)
    {
        value = null;
        if (root is null)
            return false;
        return TryResolvePath(root, jsonPath, out value, out _, out _);
    }

    /// <summary>Like <see cref="TryResolve"/> but returns diagnostics when resolution fails.</summary>
    public static bool TryResolvePath(
        JsonNode root,
        string path,
        out JsonNode? value,
        out string? error,
        out IReadOnlyList<string>? availableFields)
    {
        value = null;
        error = null;
        availableFields = null;

        var parts = ParsePath(path);
        JsonNode? current = root;
        foreach (var part in parts)
        {
            if (current is JsonObject obj)
            {
                if (part.Length >= 2 && part[0] == '[' && part[^1] == ']')
                {
                    error = $"Path not found at segment '{part}' in '{path}', see available fields";
                    availableFields = CollectAvailableFields(obj, "");
                    return false;
                }

                if (!obj.TryGetPropertyValue(part, out var next))
                {
                    error = $"Path not found at segment '{part}' in '{path}', see available fields";
                    availableFields = CollectAvailableFields(obj, "");
                    return false;
                }

                current = next;
            }
            else if (current is JsonArray arr)
            {
                if (!TryParseArrayIndex(part, out var idx))
                {
                    error = $"Expected list index at segment '{part}'";
                    availableFields = CollectAvailableFieldsFromArray(arr, "");
                    return false;
                }

                if (idx < 0 || idx >= arr.Count)
                {
                    error = $"Index out of range at segment '{part}'";
                    availableFields = CollectAvailableFieldsFromArray(arr, "");
                    return false;
                }

                current = arr[idx];
            }
            else
            {
                error = $"Cannot descend into {DescribeNodeKind(current)} at segment '{part}'";
                return false;
            }
        }

        value = current;
        return true;
    }

    private static List<string> ParsePath(string path)
    {
        var normalized = path.Replace('/', '.');
        var rawParts = new List<string>();
        foreach (var seg in normalized.Split('.'))
        {
            if (seg.Length > 0)
                rawParts.Add(seg);
        }

        var parts = new List<string>();
        foreach (var seg in rawParts)
            parts.AddRange(ParsePathSegment(seg));

        return parts;
    }

    private static List<string> ParsePathSegment(string seg)
    {
        var result = new List<string>();
        var buf = new StringBuilder();
        for (var i = 0; i < seg.Length;)
        {
            if (seg[i] == '[')
            {
                if (buf.Length > 0)
                {
                    result.Add(buf.ToString());
                    buf.Clear();
                }

                var close = seg.IndexOf(']', i);
                if (close < 0)
                {
                    buf.Append(seg.AsSpan(i));
                    break;
                }

                result.Add(seg.Substring(i, close - i + 1));
                i = close + 1;
            }
            else
            {
                buf.Append(seg[i]);
                i++;
            }
        }

        if (buf.Length > 0)
            result.Add(buf.ToString());

        return result;
    }

    private static bool TryParseArrayIndex(string part, out int idx)
    {
        idx = 0;
        if (part.Length >= 2 && part[0] == '[' && part[^1] == ']')
            return int.TryParse(part.AsSpan(1, part.Length - 2), out idx);
        return int.TryParse(part, out idx);
    }

    private static List<string> CollectAvailableFields(JsonObject node, string prefix)
    {
        var fields = new List<string>();
        foreach (var (key, val) in node)
        {
            var p = string.IsNullOrEmpty(prefix) ? key : prefix + "." + key;
            fields.Add(p);
            switch (val)
            {
                case JsonObject o:
                    fields.AddRange(CollectAvailableFields(o, p));
                    break;
                case JsonArray a:
                    fields.AddRange(CollectAvailableFieldsFromArray(a, p));
                    break;
            }
        }

        return fields;
    }

    private static List<string> CollectAvailableFieldsFromArray(JsonArray node, string prefix)
    {
        var fields = new List<string>();
        for (var i = 0; i < node.Count; i++)
        {
            var p = $"{prefix}[{i}]";
            fields.Add(p);
            switch (node[i])
            {
                case JsonObject o:
                    fields.AddRange(CollectAvailableFields(o, p));
                    break;
                case JsonArray a:
                    fields.AddRange(CollectAvailableFieldsFromArray(a, p));
                    break;
            }
        }

        return fields;
    }

    private static string DescribeNodeKind(JsonNode? n) =>
        n switch
        {
            null => "null",
            JsonValue => "scalar",
            JsonArray => "array",
            JsonObject => "object",
            _ => n.GetType().Name
        };
}
