using System.Text.Json.Nodes;
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
        var r = await registry.QueryAsync(expr, limit, cancellationToken).ConfigureAwait(false);
        if (r.Error is not null)
            return new QueryResult(new List<object>(), r.Error);
        var list = new List<object>(r.Results.Count);
        foreach (var o in r.Results)
            list.Add(o);
        return new QueryResult(list, null);
    }
}
