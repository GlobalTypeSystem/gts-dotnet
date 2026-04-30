using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;

namespace Gts.Store;

/// <summary>
/// GTS query language: base GTS id or wildcard pattern, optional <c>[prop="value", …]</c> filters (AND).
/// Attribute filters are not allowed on type patterns (suffix <c>~</c> or <c>~*</c>). Same semantics as the <c>gts query</c> CLI and <c>GET /query</c> API.
/// </summary>
public static class GtsQuery
{
    /// <summary>Parses a query expression. Returns false with <paramref name="error"/> when the expression is invalid.</summary>
    public static bool TryParse(
        string expr,
        [NotNullWhen(true)] out GtsParsedQuery? query,
        [NotNullWhen(false)] out string? error)
    {
        query = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expr))
        {
            error = "Invalid query";
            return false;
        }

        var basePart = expr;
        string? filterPart = null;
        var idx = expr.IndexOf('[', StringComparison.Ordinal);
        if (idx >= 0)
        {
            if (!expr.EndsWith("]", StringComparison.Ordinal))
            {
                error = "Invalid query: missing closing bracket ']'";
                return false;
            }

            basePart = expr[..idx].Trim();
            filterPart = expr[(idx + 1)..^1];
        }

        var filters = ParseFilters(filterPart);
        if (filters.Count > 0)
        {
            if (basePart.EndsWith("~", StringComparison.Ordinal) || basePart.EndsWith("~*", StringComparison.Ordinal))
            {
                error = "Invalid query: filters cannot be used with type patterns (ending with ~ or ~*)";
                return false;
            }
        }

        var isWildcard = basePart.Contains('*', StringComparison.Ordinal);
        if (isWildcard)
        {
            if (!(basePart.EndsWith(".*", StringComparison.Ordinal) || basePart.EndsWith("~*", StringComparison.Ordinal)))
            {
                error = "Invalid query: wildcard patterns must end with .* or ~*";
                return false;
            }

            if (!GtsId.TryParsePattern(basePart, out var w) || w is null)
            {
                error = "Invalid query";
                return false;
            }
        }
        else
        {
            if (!GtsId.TryParse(basePart, out var exact) || exact is null)
            {
                error = "Invalid query";
                return false;
            }
        }

        query = new GtsParsedQuery(basePart, isWildcard, filters);
        return true;
    }

    /// <summary>
    /// Returns identifiers from <paramref name="ids"/> whose string form matches the query base pattern.
    /// Fails when the expression includes attribute filters (those require JSON; use <see cref="Execute"/> or <see cref="GtsRegistry.QueryAsync"/>).
    /// </summary>
    public static bool TryFilterIdentifiers(
        string expr,
        IEnumerable<GtsId> ids,
        int limit,
        [NotNullWhen(true)] out List<GtsId>? matched,
        [NotNullWhen(false)] out string? error)
    {
        matched = null;
        if (!TryParse(expr, out var q, out var parseError) || q is null)
        {
            error = parseError ?? "Invalid query";
            return false;
        }

        if (q.Filters.Count > 0)
        {
            error = "Invalid query: attribute filters require entity JSON; use QueryAsync or GtsQuery.Execute with entities.";
            return false;
        }

        var lim = NormalizeLimit(limit);
        var list = new List<GtsId>();
        foreach (var id in ids)
        {
            if (list.Count >= lim)
                break;
            if (IdMatches(id, q))
                list.Add(id);
        }

        matched = list;
        error = null;
        return true;
    }

    /// <summary>Runs a parsed query over in-memory entities (same matching rules as the registry).</summary>
    public static List<JsonObject> Execute(
        IEnumerable<GtsJsonEntity> entities,
        GtsParsedQuery query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var lim = NormalizeLimit(limit);
        var results = new List<JsonObject>();
        foreach (var e in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= lim)
                break;
            if (e.GtsId is null)
                continue;
            if (!IdMatches(e.GtsId, query))
                continue;
            if (!MatchFilters(e.Content, query.Filters))
                continue;
            results.Add((JsonObject)JsonNode.Parse(e.Content.ToJsonString())!);
        }

        return results;
    }

    /// <summary>Same rule as <c>GET /query</c>: use <paramref name="limit"/> when it is 1–1000; otherwise 100.</summary>
    internal static int NormalizeLimit(int limit) => limit is >= 1 and <= 1000 ? limit : 100;

    internal static bool IdMatches(GtsId id, GtsParsedQuery q) =>
        q.IsWildcard ? id.Matches(q.BasePattern) : string.Equals(id.Id, q.BasePattern, StringComparison.Ordinal);

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

    private static bool MatchFilters(JsonObject content, IReadOnlyDictionary<string, string> filters)
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
            JsonValue jv when jv.TryGetValue<int>(out var i) => i.ToString(CultureInfo.InvariantCulture),
            JsonValue jv when jv.TryGetValue<long>(out var l) => l.ToString(CultureInfo.InvariantCulture),
            JsonValue jv when jv.TryGetValue<double>(out var d) => d.ToString("G", CultureInfo.InvariantCulture),
            null => "",
            _ => node.ToJsonString().Trim('"')
        };
    }
}
