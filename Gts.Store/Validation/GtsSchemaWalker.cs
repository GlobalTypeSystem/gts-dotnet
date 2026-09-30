using System.Text.Json.Nodes;

namespace Gts.Store.Validation;

/// <summary>
/// Visits JSON Schema positions while excluding literal annotation data such as <c>const</c>,
/// <c>default</c>, and <c>examples</c>. JsonSchema.Net does not expose its raw schema-node
/// traversal before dialect selection, nor does it allow replacing an official dialect's
/// handlers. GTS needs this traversal both to enforce placement rules for <c>x-gts-*</c>
/// keywords and to remove already-validated nested <c>$schema</c> declarations from the
/// evaluation clone so every resource keeps the bounded GTS regex handlers. Keeping that
/// knowledge here avoids multiple feature-specific walkers drifting apart.
/// </summary>
internal static class GtsSchemaWalker
{
    private static readonly string[] SchemaMaps = ["properties", "patternProperties", "definitions", "$defs", "dependentSchemas"];
    private static readonly string[] SchemaArrays = ["allOf", "anyOf", "oneOf", "prefixItems"];
    private static readonly string[] SingleSchemas = ["additionalItems", "additionalProperties", "contains", "contentSchema", "else", "if", "not", "propertyNames", "then", "unevaluatedItems", "unevaluatedProperties"];

    internal static void Visit(JsonNode? node, Action<JsonObject, bool> visitor, bool includeTraitsSchema = false) =>
        Visit(node, visitor, true, includeTraitsSchema);

    private static void Visit(JsonNode? node, Action<JsonObject, bool> visitor, bool root, bool includeTraitsSchema)
    {
        if (node is not JsonObject schema)
            return;

        visitor(schema, root);

        foreach (var keyword in SchemaMaps)
            if (schema[keyword] is JsonObject map)
                foreach (var child in map.Select(property => property.Value))
                    Visit(child, visitor, false, includeTraitsSchema);

        foreach (var keyword in SchemaArrays)
            if (schema[keyword] is JsonArray array)
                foreach (var child in array)
                    Visit(child, visitor, false, includeTraitsSchema);

        if (schema["items"] is JsonArray tupleItems)
            foreach (var child in tupleItems)
                Visit(child, visitor, false, includeTraitsSchema);
        else
            Visit(schema["items"], visitor, false, includeTraitsSchema);

        foreach (var keyword in SingleSchemas)
            Visit(schema[keyword], visitor, false, includeTraitsSchema);

        if (schema["dependencies"] is JsonObject dependencies)
            foreach (var child in dependencies.Select(property => property.Value).OfType<JsonObject>())
                Visit(child, visitor, false, includeTraitsSchema);

        if (includeTraitsSchema)
            Visit(schema[GtsSchemaKeywords.TraitsSchema], visitor, false, true);
    }
}
