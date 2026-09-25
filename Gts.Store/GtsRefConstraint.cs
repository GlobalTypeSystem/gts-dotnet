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
        return Pattern.EndsWith('*')
            ? value.StartsWith(Pattern[..^1], StringComparison.Ordinal)
            : value.StartsWith(Pattern, StringComparison.Ordinal);
    }
}