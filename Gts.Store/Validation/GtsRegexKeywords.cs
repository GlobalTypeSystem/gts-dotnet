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

        try
        {
            return GtsRegexCompiler.Compile(value.GetString()!);
        }
        catch (GtsRegexException ex)
        {
            throw new JsonSchemaException(ex.Message);
        }
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
            try
            {
                // The base PatternPropertiesKeyword looks the compiled value up by the
                // JSON-pointer-escaped pattern (as it appears in the evaluation path), so the
                // dictionary MUST be keyed that way. The regex itself compiles from the raw name.
                regexes.Add(GtsJsonPointer.Escape(property.Name), GtsRegexCompiler.Compile(property.Name));
            }
            catch (GtsRegexException ex)
            {
                throw new JsonSchemaException(ex.Message);
            }
        }
        return regexes;
    }
}

/// <summary>
/// The base keyword compiles sibling <c>patternProperties</c> names with .NET's ECMAScript engine,
/// which rejects in-profile expressions (e.g. astral ranges). Those regexes are unused: evaluation
/// reads the <c>patternProperties</c> annotation. So build without them.
/// </summary>
internal sealed class GtsAdditionalPropertiesKeyword : AdditionalPropertiesKeyword
{
    internal static GtsAdditionalPropertiesKeyword Handler { get; } = new();

    private GtsAdditionalPropertiesKeyword() { }

    public override void BuildSubschemas(KeywordData keyword, BuildContext context)
    {
        if (context.LocalSchema.ValueKind is JsonValueKind.Object && context.LocalSchema.TryGetProperty("patternProperties", out _))
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in context.LocalSchema.EnumerateObject())
                    if (property.Name != "patternProperties")
                        property.WriteTo(writer);
                writer.WriteEndObject();
            }
            context = context with { LocalSchema = JsonDocument.Parse(stream.ToArray()).RootElement };
        }
        base.BuildSubschemas(keyword, context);
    }
}

internal static class GtsRegexDialect
{
    internal static Dialect WithBoundedPatterns(Dialect dialect) => dialect
        .With([GtsPatternKeyword.Handler, GtsPatternPropertiesKeyword.Handler, GtsAdditionalPropertiesKeyword.Handler]);
}
