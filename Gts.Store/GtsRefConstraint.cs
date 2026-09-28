namespace Gts.Store;

internal readonly record struct GtsRefConstraint(string Pattern)
{
    internal static bool TryCreate(string operand, string selectedTypeId, out GtsRefConstraint constraint)
    {
        var pattern = operand == "/$id" ? selectedTypeId : GtsConstants.StripUriPrefix(operand);
        if (operand.StartsWith('/') && operand != "/$id" || !pattern.StartsWith(GtsConstants.IdPrefix, StringComparison.Ordinal))
        {
            constraint = default;
            return false;
        }
        var valid = pattern.Contains('*')
            ? pattern.Count(character => character == '*') == 1 && pattern.EndsWith('*')
            : GtsId.TryParse(pattern, out _);
        constraint = new GtsRefConstraint(pattern);
        return valid;
    }

    internal bool Matches(string value)
    {
        if (Pattern == GtsConstants.IdPrefix + "*") return true;
        if (Pattern.EndsWith('*'))
            return value.StartsWith(Pattern[..^1], StringComparison.Ordinal);
        if (!value.StartsWith(Pattern, StringComparison.Ordinal))
            return false;
        // Prefix matching alone ignores segment boundaries: an exact constraint such as
        // "gts.a.b.c.d.v1~x.y.z.w.v1" would otherwise also accept "…w.v12" or "…w.v1.5".
        // Type patterns (ending with '~') admit derived identifiers; any other (exact) pattern
        // requires a full match or a '~' segment boundary immediately after the pattern.
        return value.Length == Pattern.Length || Pattern.EndsWith('~') || value[Pattern.Length] == '~';
    }
}