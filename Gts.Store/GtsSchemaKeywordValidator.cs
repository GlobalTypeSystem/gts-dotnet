using System.Text.Json.Nodes;
using Gts.Store.Validation;

namespace Gts.Store;

public static class GtsSchemaKeywordValidator
{
    private static readonly HashSet<string> TopLevel =
    [
        GtsSchemaKeywords.Final, GtsSchemaKeywords.Abstract, GtsSchemaKeywords.Traits, GtsSchemaKeywords.TraitsSchema
    ];

    // Property-level GTS keywords that are part of the supported vocabulary (§9.6). Any other
    // `x-gts-*` key (including the removed `x-gts-ref`) is an unknown keyword and rejected (§11.0).
    private static readonly HashSet<string> Recognized = [.. GtsSchemaKeywords.ReferenceKeywords];

    public static IReadOnlyList<string> Validate(JsonObject schema)
    {
        var errors = new List<string>();
        GtsSchemaWalker.Visit(schema, (node, root) => ValidateNode(node, root, errors), includeTraitsSchema: true);
        if (schema[GtsSchemaKeywords.Final] is JsonNode final && !TryBoolean(final, out _))
            errors.Add("x-gts-final must be a boolean");
        if (schema[GtsSchemaKeywords.Abstract] is JsonNode abstractNode && !TryBoolean(abstractNode, out _))
            errors.Add("x-gts-abstract must be a boolean");
        if (TryBoolean(schema[GtsSchemaKeywords.Final], out var isFinal) && isFinal &&
            TryBoolean(schema[GtsSchemaKeywords.Abstract], out var isAbstract) && isAbstract)
            errors.Add("schema cannot declare both x-gts-final and x-gts-abstract as true");
        return errors;
    }

    private static void ValidateNode(JsonObject schema, bool root, List<string> errors)
    {
        if (schema.TryGetPropertyValue("type", out var typeNode) &&
            (typeNode is not JsonValue && typeNode is not JsonArray ||
             typeNode is JsonValue typeValue && !typeValue.TryGetValue<string>(out _) ||
             typeNode is JsonArray typeArray && typeArray.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _))))
            errors.Add("JSON Schema type must be a string or an array of strings");

        foreach (var key in schema.Select(property => property.Key))
        {
            if (key.StartsWith(GtsSchemaKeywords.Prefix, StringComparison.Ordinal) && !Recognized.Contains(key) && !TopLevel.Contains(key))
                errors.Add($"Unsupported GTS extension keyword: {key}");
            if (!root && TopLevel.Contains(key))
                errors.Add($"{key} must be at the schema top level");
        }
    }

    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value);
    }
}
