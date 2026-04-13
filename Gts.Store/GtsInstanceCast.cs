using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Transforms JSON instances toward a target JSON Schema (GTS cast / minor version migration).</summary>
public static class GtsInstanceCast
{
    /// <summary>Returns a deep-cloned instance updated toward <paramref name="targetEffective"/>.</summary>
    public static JsonObject CastToEffectiveSchema(JsonObject instance, JsonObject targetEffective)
    {
        var result = (JsonObject)instance.DeepClone()!;
        _ = CastObject(result, targetEffective, "");
        return result;
    }

    private static void CastObject(JsonObject result, JsonObject schema, string basePath)
    {
        var targetProps = schema["properties"] as JsonObject ?? new JsonObject();
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema["required"] is JsonArray rq)
        {
            foreach (var x in rq)
            {
                if (x is JsonValue jv && jv.TryGetValue<string>(out var name))
                    required.Add(name);
            }
        }

        var additional = true;
        if (schema.TryGetPropertyValue("additionalProperties", out var apNode) && apNode is JsonValue apv)
        {
            if (apv.TryGetValue<bool>(out var ab))
                additional = ab;
        }

        foreach (var prop in required)
        {
            if (result.TryGetPropertyValue(prop, out _))
                continue;

            if (targetProps.TryGetPropertyValue(prop, out var pSchema) && pSchema is JsonObject pso &&
                pso.TryGetPropertyValue("default", out var def))
            {
                result[prop] = def.DeepClone();
            }
        }

        foreach (var (prop, pSchema) in targetProps)
        {
            if (required.Contains(prop))
                continue;
            if (result.ContainsKey(prop))
                continue;
            if (pSchema is JsonObject pso && pso.TryGetPropertyValue("default", out var def))
                result[prop] = def.DeepClone();
        }

        foreach (var (prop, pSchema) in targetProps)
        {
            if (pSchema is not JsonObject pso)
                continue;
            if (pso.TryGetPropertyValue("const", out var c) && c is JsonValue cv && result.TryGetPropertyValue(prop, out var existing))
            {
                if (cv.TryGetValue<string>(out var constStr) && existing is JsonValue ev &&
                    ev.TryGetValue<string>(out var oldStr) &&
                    GtsId.TryParse(constStr, out _) && GtsId.TryParse(oldStr, out _) && constStr != oldStr)
                    result[prop] = JsonValue.Create(constStr);
            }
        }

        if (additional is false)
        {
            var toRemove = result.Select(p => p.Key).Where(k => !targetProps.ContainsKey(k)).ToList();
            foreach (var k in toRemove)
                result.Remove(k);
        }

        foreach (var (prop, pSchema) in targetProps)
        {
            if (!result.TryGetPropertyValue(prop, out var val))
                continue;
            if (pSchema is not JsonObject pso)
                continue;

            var pType = pso["type"] is JsonValue tv && tv.TryGetValue<string>(out var ts) ? ts : null;
            if (pType == "object" && val is JsonObject vo)
            {
                var nested = EffectiveObjectSchema(pso);
                CastObject(vo, nested, string.IsNullOrEmpty(basePath) ? prop : basePath + "." + prop);
            }
            else if (pType == "array" && val is JsonArray arr &&
                     pso["items"] is JsonObject items && items["type"] is JsonValue itv &&
                     itv.TryGetValue<string>(out var it) && it == "object")
            {
                var nested = EffectiveObjectSchema(items);
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject itemObj)
                        CastObject(itemObj, nested, $"{basePath}.{prop}[{i}]");
                }
            }
        }
    }

    private static JsonObject EffectiveObjectSchema(JsonObject s)
    {
        if (s["properties"] is JsonObject || s["required"] is JsonArray)
            return s;
        if (s["allOf"] is JsonArray allof)
        {
            foreach (var part in allof)
            {
                if (part is JsonObject po && (po["properties"] is JsonObject || po["required"] is JsonArray))
                    return po;
            }
        }

        return s;
    }

    /// <summary>Removes <c>const</c> where the value is a GTS id string, for tolerant validation.</summary>
    public static JsonNode RemoveGtsConstConstraints(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (k, v) in obj)
                {
                    if (k == "const" && v is JsonValue jv && jv.TryGetValue<string>(out var s) && GtsId.TryParse(s, out _))
                    {
                        copy["type"] = "string";
                        continue;
                    }

                    copy[k] = RemoveGtsConstConstraints(v);
                }

                return copy;
            case JsonArray arr:
                var na = new JsonArray();
                foreach (var item in arr)
                    na.Add(RemoveGtsConstConstraints(item));
                return na;
            case JsonValue:
                return node.DeepClone();
            default:
                return node?.DeepClone() ?? JsonValue.Create((object?)null)!;
        }
    }

    internal static JsonDocument ToJsonDocument(JsonObject o)
        => JsonDocument.Parse(o.ToJsonString());
}
