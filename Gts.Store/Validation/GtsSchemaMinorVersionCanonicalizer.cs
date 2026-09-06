using System.Text.Json.Nodes;
using Gts.Store;

namespace Gts.Store.Validation;

/// <summary>
/// Normalizes GTS JSON Schema documents so two minor versions of the same type can be compared
/// for structural equality (full compatibility): Draft-7 prep, then stable URIs with last minor stripped.
/// </summary>
internal static class GtsSchemaMinorVersionCanonicalizer
{
    internal static JsonObject PrepareForComparison(JsonObject root)
    {
        var normalized = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(root);
        var clone = JsonNode.Parse(normalized.ToJsonString())!.AsObject();
        RewriteGtsIdentifiersDeep(clone);
        return clone;
    }

    private static void RewriteGtsIdentifiersDeep(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                foreach (var (key, val) in obj.ToList())
                {
                    if (key is "$id" or "$ref" && val is JsonValue jv && jv.TryGetValue<string>(out var s)
                        && !string.IsNullOrEmpty(s))
                    {
                        if (TryCanonicalizeUriString(s.Trim(), out var rewritten))
                            obj[key] = rewritten;
                    }
                    else
                        RewriteGtsIdentifiersDeep(val);
                }

                break;
            }
            case JsonArray arr:
            {
                foreach (var item in arr)
                    RewriteGtsIdentifiersDeep(item);
                break;
            }
        }
    }

    private static bool TryCanonicalizeUriString(string s, out JsonValue rewritten)
    {
        if (s.StartsWith(GtsSchemaResolutionUris.SyntheticBase, StringComparison.Ordinal))
        {
            var encoded = s.AsSpan(GtsSchemaResolutionUris.SyntheticBase.Length);
            if (encoded.IsEmpty)
            {
                rewritten = JsonValue.Create(s)!;
                return false;
            }

            var gtsId = Uri.UnescapeDataString(encoded.ToString());
            if (gtsId.Length == 0)
            {
                rewritten = JsonValue.Create(s)!;
                return false;
            }

            var stripped = GtsTypeFamily.StripLastMinorFromTypeId(gtsId);
            if (stripped == gtsId)
            {
                rewritten = JsonValue.Create(s)!;
                return false;
            }

            rewritten = JsonValue.Create(GtsSchemaResolutionUris.SyntheticBase + Uri.EscapeDataString(stripped))!;
            return true;
        }

        const string gtsUriScheme = "gts://";
        if (s.StartsWith(gtsUriScheme, StringComparison.Ordinal))
        {
            var inner = s[gtsUriScheme.Length..];
            var stripped = GtsTypeFamily.StripLastMinorFromTypeId(inner);
            if (stripped == inner)
            {
                rewritten = JsonValue.Create(s)!;
                return false;
            }

            rewritten = JsonValue.Create(gtsUriScheme + stripped)!;
            return true;
        }

        rewritten = JsonValue.Create(s)!;
        return false;
    }
}
