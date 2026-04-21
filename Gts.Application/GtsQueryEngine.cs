using System.Text.Json.Nodes;
using Gts;
using Gts.Store;

namespace Gts.Application;

/// <summary>Executes GTS wildcard / filter queries against a registry (same semantics as <c>/query</c>).</summary>
public static class GtsQueryEngine
{
    public sealed record QueryResult(List<object> Results, string? Error);

    public static async Task<QueryResult> ExecuteAsync(
        GtsRegistry registry,
        string expr,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var basePart = expr;
        string? filterPart = null;
        var idx = expr.IndexOf('[');
        if (idx >= 0)
        {
            if (!expr.EndsWith("]", StringComparison.Ordinal))
                return new QueryResult(new List<object>(), "Invalid query");
            basePart = expr[..idx].Trim();
            filterPart = expr[(idx + 1)..^1];
        }

        var filters = ParseFilters(filterPart);
        var isWildcard = basePart.Contains('*', StringComparison.Ordinal);

        if (isWildcard)
        {
            if (!(basePart.EndsWith(".*", StringComparison.Ordinal) || basePart.EndsWith("~*", StringComparison.Ordinal)))
                return new QueryResult(new List<object>(), "Invalid query: wildcard patterns must end with .* or ~*");
            if (!GtsId.TryParsePattern(basePart, out var w) || w is null)
                return new QueryResult(new List<object>(), "Invalid query");
        }
        else
        {
            if (!GtsId.TryParse(basePart, out var exact) || exact is null)
                return new QueryResult(new List<object>(), "Invalid query");
        }

        var results = new List<object>();
        var all = await registry.GetAllAsync().ConfigureAwait(false);
        foreach (var e in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= limit)
                break;
            if (e.GtsId is null)
                continue;

            bool match;
            if (isWildcard)
                match = e.GtsId.Matches(basePart);
            else
                match = string.Equals(e.GtsId.Id, basePart, StringComparison.Ordinal);

            if (!match)
                continue;
            if (!MatchFilters(e.Content, filters))
                continue;
            results.Add(JsonNode.Parse(e.Content.ToJsonString())!);
        }

        return new QueryResult(results, null);
    }

    private static Dictionary<string, string> ParseFilters(string? filterPart)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(filterPart))
            return d;

        foreach (var part in filterPart.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim();
            var eq = p.IndexOf('=');
            if (eq <= 0)
                continue;
            var k = p[..eq].Trim();
            var v = p[(eq + 1)..].Trim().Trim('"').Trim('\'');
            d[k] = v;
        }

        return d;
    }

    private static bool MatchFilters(JsonObject content, Dictionary<string, string> filters)
    {
        foreach (var (k, v) in filters)
        {
            if (!content.TryGetPropertyValue(k, out var node))
                return false;
            var ev = JsonLeaf(node);
            if (v == "*")
            {
                if (string.IsNullOrEmpty(ev))
                    return false;
            }
            else if (ev != v)
            {
                return false;
            }
        }

        return true;
    }

    private static string JsonLeaf(JsonNode? node)
    {
        return node switch
        {
            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
            JsonValue jv when jv.TryGetValue<bool>(out var b) => b ? "true" : "false",
            JsonValue jv when jv.TryGetValue<int>(out var i) => i.ToString(),
            JsonValue jv when jv.TryGetValue<long>(out var l) => l.ToString(),
            JsonValue jv when jv.TryGetValue<double>(out var d) => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            null => "",
            _ => node.ToJsonString().Trim('"')
        };
    }
}
