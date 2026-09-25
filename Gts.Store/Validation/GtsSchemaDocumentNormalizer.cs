using System.Text.Json.Nodes;

namespace Gts.Store.Validation;

/// <summary>
/// Prepares GTS JSON Schema documents for Draft 7 evaluation: renames <c>$$</c> keywords and
/// rewrites <c>gts://</c> in <c>$id</c> / <c>$ref</c> to synthetic HTTPS URIs resolvable by the evaluator.
/// </summary>
internal static class GtsSchemaDocumentNormalizer
{
    private const string GtsUriPrefix = GtsConstants.UriPrefix;

    internal static JsonObject CanonicalizeKeywords(JsonObject root)
    {
        var clone = (JsonObject)root.DeepClone();
        RenameDoubleDollarKeysDeep(clone);
        return clone;
    }

    internal static JsonObject ForJsonSchemaEvaluation(JsonObject root)
    {
        var clone = (JsonObject)root.DeepClone();
        if (!clone.ContainsKey("$schema"))
            RenameDoubleDollarKeysDeep(clone);
        RewriteGtsUrisDeep(clone);
        StripXGtsRefDeep(clone);
        return clone;
    }

    private static void RenameDoubleDollarKeysDeep(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    var keys = obj.Select(p => p.Key).ToList();
                    foreach (var key in keys)
                    {
                        if (key.StartsWith("$$", StringComparison.Ordinal) && key.Length > 2)
                        {
                            var newKey = "$" + key[2..];
                            var n = obj[key]!;
                            obj.Remove(key);
                            obj[newKey] = n;
                        }
                    }

                    foreach (var p in obj)
                        RenameDoubleDollarKeysDeep(p.Value);
                    break;
                }
            case JsonArray arr:
                {
                    foreach (var item in arr)
                        RenameDoubleDollarKeysDeep(item);
                    break;
                }
        }
    }

    private static void StripXGtsRefDeep(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return;
        obj.Remove(GtsSchemaKeywords.Ref);
        foreach (var keyword in new[] { "properties", "patternProperties", "definitions", "$defs", "dependentSchemas" })
        {
            if (obj[keyword] is JsonObject map)
            {
                foreach (var child in map.Select(property => property.Value))
                    StripXGtsRefDeep(child);
            }
        }
        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf", "prefixItems" })
        {
            if (obj[keyword] is JsonArray branches)
            {
                foreach (var child in branches)
                    StripXGtsRefDeep(child);
                if (branches.Count > 0 && branches.All(branch => branch is JsonObject branchObject && branchObject.Count == 0))
                    obj.Remove(keyword);
            }
        }
        foreach (var keyword in new[] { "items", "additionalItems", "additionalProperties", "contains", "not", "if", "then", "else" })
        {
            if (obj[keyword] is JsonObject child)
                StripXGtsRefDeep(child);
        }
    }

    private static void RewriteGtsUrisDeep(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    foreach (var (key, val) in obj.ToList())
                    {
                        if (key is "$id" or "$ref" && val is JsonValue jv)
                        {
                            var s = jv.GetValue<string?>();
                            if (!string.IsNullOrEmpty(s))
                            {
                                var t = s.Trim();
                                if (t.StartsWith(GtsUriPrefix, StringComparison.Ordinal))
                                {
                                    var id = t[GtsUriPrefix.Length..];
                                    if (id.Length > 0)
                                        obj[key] = GtsSchemaResolutionUris.ToSyntheticUri(id).AbsoluteUri;
                                }
                            }
                        }
                        else if (key == "$schema" && val is JsonValue schemaValue)
                        {
                            var dialect = schemaValue.GetValue<string?>()?.Trim();
                            if (dialect is "https://json-schema.org/draft-07/schema" or "https://json-schema.org/draft-07/schema#")
                                obj[key] = "http://json-schema.org/draft-07/schema#";
                        }
                    }

                    foreach (var p in obj)
                        RewriteGtsUrisDeep(p.Value);
                    break;
                }
            case JsonArray arr:
                {
                    foreach (var item in arr)
                        RewriteGtsUrisDeep(item);
                    break;
                }
        }
    }
}
