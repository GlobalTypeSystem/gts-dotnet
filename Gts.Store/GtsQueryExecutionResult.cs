using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Outcome of <see cref="GtsRegistry.QueryAsync"/> (GTS query language, OP#10).</summary>
public sealed record GtsQueryExecutionResult(int Limit, string? Error, IReadOnlyList<JsonObject> Results)
{
    /// <summary>True when <see cref="Error"/> is null.</summary>
    public bool Ok => Error is null;

    /// <summary>Failed parse or validation.</summary>
    public static GtsQueryExecutionResult Failed(int limit, string error) => new(limit, error, Array.Empty<JsonObject>());

    /// <summary>Successful query with matching entity bodies (up to <paramref name="limit"/>).</summary>
    public static GtsQueryExecutionResult Success(int limit, IReadOnlyList<JsonObject> results) => new(limit, null, results);
}
