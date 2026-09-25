using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

/// <summary>Validates <c>$ref</c> shape in JSON Schema documents (local <c>#</c> or <c>gts://</c> only).</summary>
public static class GtsSchemaRefFormatValidator
{
    /// <summary>Throws <see cref="InvalidOperationException"/> when an invalid <c>$ref</c> is found.</summary>
    public static void ValidateRefs(JsonNode? node, string path = "") => ValidateRefs(node, node, path);

    private static void ValidateRefs(JsonNode? node, JsonNode? root, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("$ref", out var r) && r is JsonValue rv && rv.TryGetValue<string>(out var refUri))
                {
                    var currentPath = string.IsNullOrEmpty(path) ? "$ref" : path + ".$ref";
                    if (refUri.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (!GtsJsonPointer.TryEvaluate(root, refUri, out _))
                            throw new InvalidOperationException($"Invalid $ref at '{currentPath}': local reference target '{refUri}' not found.");
                    }
                    else if (refUri.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
                    {
                        var gtsId = GtsConstants.StripUriPrefix(refUri).Split('#')[0];
                        if (!GtsId.TryParse(gtsId, out _) && !GtsId.TryParsePattern(gtsId, out _))
                            throw new InvalidOperationException(
                                $"Invalid $ref at '{currentPath}': '{refUri}' contains invalid GTS identifier '{gtsId}'.");
                    }
                    else
                        throw new InvalidOperationException(
                            $"Invalid $ref at '{currentPath}': '{refUri}' must be a local ref (starting with '#') or a GTS URI (starting with 'gts://').");
                }

                foreach (var (key, value) in obj)
                {
                    if (key == "$ref")
                        continue;
                    var nested = string.IsNullOrEmpty(path) ? key : path + "." + key;
                    ValidateRefs(value, root, nested);
                }
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    ValidateRefs(array[index], root, $"{path}[{index}]");
                break;
        }
    }

}
