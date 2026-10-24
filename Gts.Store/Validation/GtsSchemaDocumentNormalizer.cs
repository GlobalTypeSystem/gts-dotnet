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

    internal static void RemoveDialectDeclarations(JsonObject root) =>
        GtsSchemaWalker.Visit(root, static (schema, _) => schema.Remove("$schema"), includeTraitsSchema: true);

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
        foreach (var refKeyword in GtsSchemaKeywords.ReferenceKeywords)
            obj.Remove(refKeyword);
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
                // The JSON Schema engine cannot see x-gts-ref (it is stripped here and checked
                // separately by GtsRefValidator). Two oneOf branches that differ only by x-gts-ref
                // — e.g. {"type":"string","x-gts-ref":"a.*"} and {…"b.*"} — collapse to the same
                // structural schema, so every value matches both and oneOf rejects everything.
                // Evaluate such a oneOf as anyOf: exclusivity is enforced by GtsRefValidator.
                //
                // NOTE: x-gts-ref is still enforced by GtsRefValidator rather than by the
                // JsonSchema.Net dialect. This localized rewrite keeps oneOf branch behavior
                // consistent until x-gts-ref is migrated to a first-class keyword handler.
                var hadRef = branches.OfType<JsonObject>().Any(branch => GtsSchemaKeywords.ReferenceKeywords.Any(branch.ContainsKey));
                foreach (var child in branches)
                    StripXGtsRefDeep(child);
                if (branches.Count > 0 && branches.All(branch => branch is JsonObject branchObject && branchObject.Count == 0))
                    obj.Remove(keyword);
                else if (keyword == "oneOf" && hadRef && !obj.ContainsKey("anyOf"))
                {
                    obj.Remove("oneOf");
                    obj["anyOf"] = branches;
                }
            }
        }
        // A conditional whose `if` carries a reference keyword is dropped from the engine's view for
        // the same reason as `not` below: stripped, `if` would hold for every value and `then` would
        // always apply. GtsRefValidator selects and checks the active branch instead.
        if (ContainsReferenceKeyword(obj["if"]))
        {
            obj.Remove("if");
            obj.Remove("then");
            obj.Remove("else");
        }
        foreach (var keyword in new[] { "items", "additionalItems", "additionalProperties", "contains", "not", "if", "then", "else" })
        {
            if (obj[keyword] is not JsonObject child)
                continue;
            var hadRef = ContainsReferenceKeyword(child);
            StripXGtsRefDeep(child);
            // `not` whose subschema carries a reference keyword is dropped from the engine's view:
            // the engine cannot see reference keywords, so a negated subschema like
            // {"type":"string","x-gts-pattern-ref":...} would collapse to "not a string" and reject
            // every string. GtsRefValidator applies the full `not` inversion (including the
            // structural part that only constrains strings) against the unstripped schema instead.
            if (keyword == "not" && hadRef)
                obj.Remove("not");
        }
    }

    private static bool ContainsReferenceKeyword(JsonNode? node) => node switch
    {
        JsonObject obj => GtsSchemaKeywords.ReferenceKeywords.Any(obj.ContainsKey) || obj.Any(property => ContainsReferenceKeyword(property.Value)),
        JsonArray array => array.Any(ContainsReferenceKeyword),
        _ => false,
    };

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
