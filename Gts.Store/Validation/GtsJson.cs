using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gts.Store.Validation;

internal static class GtsJson
{
    internal static JsonElement ToElement(JsonNode? node) => JsonSerializer.SerializeToElement(node);

    internal static JsonObject CloneObject(JsonObject source) => (JsonObject)source.DeepClone();
}