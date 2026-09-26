using System.Text.Json.Nodes;
using Gts.Store.Validation;

namespace Gts.Store;

internal static class GtsSchemaDependencyGraph
{
    internal static string Dialect(JsonObject schema)
    {
        var value = schema["$schema"]?.GetValue<string>() ?? "http://json-schema.org/draft-07/schema#";
        if (value.Contains("draft-07", StringComparison.Ordinal)) return "draft-07";
        if (value.Contains("2019-09", StringComparison.Ordinal)) return "2019-09";
        if (value.Contains("2020-12", StringComparison.Ordinal)) return "2020-12";
        return value.TrimEnd('#');
    }

    internal static JsonObject Resolve(JsonObject schema, Func<GtsId, JsonObject?> load) => Resolve(schema, load, new HashSet<string>(), 0);

    internal static IEnumerable<string> References(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
                yield return GtsConstants.StripUriPrefix(reference).Split('#')[0];
            foreach (var child in obj.Select(property => property.Value))
            {
                foreach (var nested in References(child))
                    yield return nested;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                foreach (var nested in References(child))
                    yield return nested;
            }
        }
    }

    internal static bool HasCycle(string id, JsonObject schema, Func<GtsId, JsonObject?> load) =>
        HasCycle(id, schema, load, new HashSet<string>(), new HashSet<string>());

    internal static void ValidateDialects(JsonNode? node, string rootDialect, Func<GtsId, JsonObject?> load, List<string> errors)
    {
        if (node is JsonObject obj)
        {
            if (obj["$schema"] is JsonValue && Dialect(obj) != rootDialect)
                errors.Add("Embedded JSON Schema resource uses a different dialect from the root schema");
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
            {
                var id = GtsConstants.StripUriPrefix(reference).Split('#')[0];
                if (!GtsId.TryParse(id, out var parsed) || parsed is null || load(parsed) is not JsonObject target)
                    errors.Add($"Referenced GTS Type Schema '{id}' not found");
                else if (rootDialect != Dialect(target))
                    errors.Add($"Referenced GTS Type Schema '{id}' uses a different JSON Schema dialect");
            }
            foreach (var child in obj.Select(property => property.Value))
                ValidateDialects(child, rootDialect, load, errors);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                ValidateDialects(child, rootDialect, load, errors);
        }
    }

    private static bool HasCycle(string id, JsonObject schema, Func<GtsId, JsonObject?> load, HashSet<string> visiting, HashSet<string> visited)
    {
        if (!visiting.Add(id)) return true;
        if (visited.Contains(id))
        {
            visiting.Remove(id);
            return false;
        }
        foreach (var reference in References(schema))
        {
            if (GtsId.TryParse(reference, out var parsed) && parsed is not null && load(parsed) is JsonObject target && HasCycle(reference, target, load, visiting, visited))
                return true;
        }
        visiting.Remove(id);
        visited.Add(id);
        return false;
    }

    private static JsonObject Resolve(JsonObject schema, Func<GtsId, JsonObject?> load, HashSet<string> stack, int depth)
    {
        // Inlining $ref targets can build a tree deeper than any single input document (a chain of N
        // distinct type schemas resolves to depth ~N), so cap it independently of the JSON parser's
        // own depth limit. Beyond the cap we stop inlining and return the node as-is.
        if (depth >= GtsConstants.MaxNestingDepth)
            return (JsonObject)schema.DeepClone();

        if (schema["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
        {
            // A $ref may carry a JSON Pointer fragment (e.g. "gts://…~#/definitions/address") that
            // selects a subschema of the target, and — under 2019-09/2020-12 — may sit next to sibling
            // keywords that also apply. Evaluate the fragment against the loaded document and preserve
            // any siblings by composing them with the resolved target via allOf.
            var parts = GtsConstants.StripUriPrefix(reference).Split('#', 2);
            var id = parts[0];
            if (GtsId.TryParse(id, out var parsed) && parsed is not null && stack.Add(reference) && load(parsed) is JsonObject document)
            {
                JsonObject? target = parts.Length == 2 && parts[1].Length > 0
                    ? (GtsJsonPointer.TryEvaluate(document, "#" + parts[1], out var fragment) ? fragment as JsonObject : null)
                    : document;
                if (target is not null)
                {
                    var resolved = Resolve(target, load, stack, depth + 1);
                    stack.Remove(reference);
                    var siblings = new JsonObject();
                    foreach (var (key, value) in schema)
                        if (key != "$ref") siblings[key] = value?.DeepClone();
                    if (siblings.Count == 0)
                        return resolved;
                    return new JsonObject { ["allOf"] = new JsonArray(resolved, Resolve(siblings, load, stack, depth + 1)) };
                }
                stack.Remove(reference);
            }
        }
        var clone = new JsonObject();
        foreach (var (key, value) in schema)
            clone[key] = value switch
            {
                JsonObject obj => Resolve(obj, load, stack, depth + 1),
                JsonArray array => new JsonArray(array.Select(item => item is JsonObject child ? Resolve(child, load, stack, depth + 1) : item?.DeepClone()).ToArray()),
                _ => value?.DeepClone()
            };
        return clone;
    }
}