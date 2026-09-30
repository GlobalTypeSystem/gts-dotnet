using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;
using Json.Schema.Keywords;

namespace Gts.Store.Validation;

internal sealed class GtsPatternKeyword : PatternKeyword
{
    internal static GtsPatternKeyword Handler { get; } = new();

    private GtsPatternKeyword() { }

    public override object? ValidateKeywordValue(JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.String)
            throw new JsonSchemaException($"'{Name}' value must be a string, found {value.ValueKind}");

        return new Regex(value.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }
}

internal sealed class GtsPatternPropertiesKeyword : PatternPropertiesKeyword
{
    internal static GtsPatternPropertiesKeyword Handler { get; } = new();

    private GtsPatternPropertiesKeyword() { }

    public override object? ValidateKeywordValue(JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Object)
            throw new JsonSchemaException($"'{Name}' value must be an object, found {value.ValueKind}");

        var regexes = new Dictionary<string, Regex>();
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
                throw new JsonSchemaException("Values must be valid schemas");
            regexes.Add(property.Name, new Regex(property.Name, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)));
        }
        return regexes;
    }
}

internal static class GtsRegexDialect
{
    internal static Dialect WithBoundedPatterns(Dialect dialect) => dialect
        .With([GtsPatternKeyword.Handler, GtsPatternPropertiesKeyword.Handler]);
}
