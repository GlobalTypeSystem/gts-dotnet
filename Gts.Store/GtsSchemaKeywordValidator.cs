using System.Text.Json.Nodes;

namespace Gts.Store;

public static class GtsSchemaKeywordValidator
{
    private static readonly HashSet<string> TopLevel =
    [
        GtsSchemaKeywords.Final, GtsSchemaKeywords.Abstract, GtsSchemaKeywords.Traits, GtsSchemaKeywords.TraitsSchema
    ];

    public static IReadOnlyList<string> Validate(JsonObject schema)
    {
        var errors = new List<string>();
        ValidateNode(schema, true, errors);
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
            if (key.StartsWith(GtsSchemaKeywords.Prefix, StringComparison.Ordinal) && key != GtsSchemaKeywords.Ref && !TopLevel.Contains(key))
                errors.Add($"Unsupported GTS extension keyword: {key}");
            if (!root && TopLevel.Contains(key))
                errors.Add($"{key} must be at the schema top level");
        }

        RecurseMap(schema["properties"], errors);
        RecurseMap(schema["patternProperties"], errors);
        RecurseMap(schema["definitions"], errors);
        RecurseMap(schema["$defs"], errors);
        RecurseMap(schema["dependentSchemas"], errors);
        foreach (var key in new[] { "additionalProperties", "additionalItems", "contains", "not", "if", "then", "else", "propertyNames", "unevaluatedProperties", "unevaluatedItems" })
            Recurse(schema[key], errors);
        foreach (var key in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
            RecurseArray(schema[key], errors);
        Recurse(schema["items"], errors);
    }

    private static void RecurseMap(JsonNode? node, List<string> errors)
    {
        if (node is not JsonObject map)
            return;
        foreach (var child in map.Select(property => property.Value).OfType<JsonObject>())
            ValidateNode(child, false, errors);
    }

    private static void RecurseArray(JsonNode? node, List<string> errors)
    {
        if (node is not JsonArray array)
            return;
        foreach (var child in array.OfType<JsonObject>())
            ValidateNode(child, false, errors);
    }

    private static void Recurse(JsonNode? node, List<string> errors)
    {
        if (node is JsonObject child)
            ValidateNode(child, false, errors);
        else
            RecurseArray(node, errors);
    }

    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value);
    }
}
