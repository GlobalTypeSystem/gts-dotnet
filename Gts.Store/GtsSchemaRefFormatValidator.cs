using System.Text.Json.Nodes;
using Gts.Extraction;

namespace Gts.Store;

/// <summary>Validates <c>$ref</c> shape in JSON Schema documents (local <c>#</c> or <c>gts://</c> only).</summary>
public static class GtsSchemaRefFormatValidator
{
    /// <summary>Throws <see cref="InvalidOperationException"/> when an invalid <c>$ref</c> is found.</summary>
    public static void ValidateRefs(JsonNode? node, string path = "")
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("$ref", out var r) && r is JsonValue rv && rv.TryGetValue<string>(out var refUri))
                {
                    var currentPath = string.IsNullOrEmpty(path) ? "$ref" : path + ".$ref";
                    if (refUri.StartsWith("#", StringComparison.Ordinal))
                    {
                        // local ref OK
                    }
                    else if (refUri.StartsWith("gts://", StringComparison.Ordinal))
                    {
                        var gtsId = refUri["gts://".Length..];
                        if (!GtsId.TryParse(gtsId, out _) && !GtsId.TryParsePattern(gtsId, out _))
                            throw new InvalidOperationException(
                                $"Invalid $ref at '{currentPath}': '{refUri}' contains invalid GTS identifier '{gtsId}'.");
                    }
                    else
                        throw new InvalidOperationException(
                            $"Invalid $ref at '{currentPath}': '{refUri}' must be a local ref (starting with '#') or a GTS URI (starting with 'gts://').");
                }

                foreach (var (k, v) in obj)
                {
                    if (k == "$ref")
                        continue;
                    var nested = string.IsNullOrEmpty(path) ? k : path + "." + k;
                    ValidateRefs(v, nested);
                }

                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    ValidateRefs(arr[i], $"{path}[{i}]");
                break;
        }
    }
}
