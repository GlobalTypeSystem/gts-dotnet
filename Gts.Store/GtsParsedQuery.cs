namespace Gts.Store;

/// <summary>Parsed GTS query expression: base id pattern (exact or wildcard) and optional JSON property filters.</summary>
public sealed record GtsParsedQuery(string BasePattern, bool IsWildcard, IReadOnlyDictionary<string, string> Filters);
