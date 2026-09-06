using System.Text.Json.Nodes;

namespace Gts.Store.Validation;

/// <summary>
/// Prepares GTS JSON Schema documents for Draft 7 evaluation: renames <c>$$</c> keywords and
/// rewrites <c>gts://</c> in <c>$id</c> / <c>$ref</c> to synthetic HTTPS URIs resolvable by the evaluator.
/// </summary>
internal static class GtsSchemaDocumentNormalizer
{
    private const string GtsUriPrefix = "gts://";

    internal static JsonObject ForJsonSchemaEvaluation(JsonObject root)
    {
        var clone = JsonNode.Parse(root.ToJsonString())!.AsObject();
        RenameDoubleDollarKeysDeep(clone);
        RewriteGtsUrisDeep(clone);
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
