using System.Text.Json.Nodes;

namespace Gts.Store;

internal static class GtsTraitComposer
{
    internal static JsonObject CollectLevelValues(JsonObject schema)
    {
        var values = new JsonObject();
        CollectValues(schema, values);
        return values;
    }

    internal static void MergePatch(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject patchObject)
            {
                var targetObject = target[key] as JsonObject ?? new JsonObject();
                MergePatch(targetObject, patchObject);
                target[key] = targetObject;
            }
            else
                target[key] = value.DeepClone();
        }
    }

    internal static JsonObject BuildEffectiveSchema(IReadOnlyList<JsonNode?> declarations, JsonNode? dialect)
    {
        var effective = declarations.Count == 1
            ? declarations[0]!.DeepClone()
            : new JsonObject { ["type"] = "object", ["allOf"] = new JsonArray(declarations.Select(node => node?.DeepClone()).ToArray()) };
        var schema = effective as JsonObject ?? new JsonObject();
        if (dialect is not null)
            schema["$schema"] = dialect.DeepClone();
        return schema;
    }

    internal static void MaterializeDefaults(JsonObject schema, JsonObject values)
    {
        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf.OfType<JsonObject>().Reverse())
                MaterializeDefaults(branch, values);
        }
        if (schema["properties"] is not JsonObject properties)
            return;
        foreach (var (name, node) in properties)
        {
            if (!values.ContainsKey(name) && node is JsonObject property && property.TryGetPropertyValue("default", out var defaultValue))
                values[name] = defaultValue?.DeepClone();
            if (values[name] is JsonObject nestedValues && node is JsonObject nestedSchema)
                MaterializeDefaults(nestedSchema, nestedValues);
        }
    }

    private static void CollectValues(JsonObject schema, JsonObject values)
    {
        if (schema[GtsSchemaKeywords.Traits] is JsonObject traits)
        {
            foreach (var (key, value) in traits)
                values[key] = value?.DeepClone();
        }
        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf.OfType<JsonObject>())
                CollectValues(branch, values);
        }
    }
}