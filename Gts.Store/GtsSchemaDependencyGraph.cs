using System.Text.Json.Nodes;
using Gts.Store.Validation;

namespace Gts.Store;

internal static class GtsSchemaDependencyGraph
{
    internal static string Dialect(JsonObject schema)
    {
        var document = !schema.ContainsKey("$schema") && schema.ContainsKey("$$schema")
            ? GtsSchemaDocumentNormalizer.CanonicalizeKeywords(schema)
            : schema;
        // An unsupported or missing dialect is a validation concern, not an internal fault: the
        // callers (ValidateDialects, HasMixedDialectReferences) compare dialects to detect a
        // cross-dialect graph and report failures via their result/error channels. Returning a
        // sentinel that can never equal a supported dialect keeps an unsupported referenced schema
        // a reported mismatch instead of an exception that escapes the validator as an internal error.
        return GtsTypeSchema.TryGetSupportedDialect(document, out var dialect, out _)
            ? dialect
            : "unsupported:" + (document["$schema"]?.ToJsonString() ?? "<missing>");
    }

    /// <summary>Upper bound on schema nodes produced by inlining, so shared references cannot blow up exponentially.</summary>
    internal const int MaxResolvedNodes = 100_000;

    /// <summary>Keywords whose values are literal data, never schemas; copied verbatim.</summary>
    private static readonly HashSet<string> LiteralKeywords = new(StringComparer.Ordinal)
    {
        "const", "enum", "default", "examples", GtsSchemaKeywords.Traits,
    };

    private static readonly HashSet<string> SchemaMapKeywords = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "definitions", "$defs", "dependentSchemas",
    };

    /// <exception cref="GtsSchemaTooLargeException">Inlining exceeds <see cref="MaxResolvedNodes"/>.</exception>
    internal static JsonObject Resolve(JsonObject schema, Func<GtsId, JsonObject?> load) =>
        Resolve(schema, load, new HashSet<string>(), 0, new Budget());

    private sealed class Budget
    {
        private int _nodes;

        internal void Spend()
        {
            if (++_nodes > MaxResolvedNodes)
                throw new GtsSchemaTooLargeException();
        }
    }

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

    private static JsonObject Resolve(JsonObject schema, Func<GtsId, JsonObject?> load, HashSet<string> stack, int depth, Budget budget)
    {
        budget.Spend();
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
                    // Local `#...` references in the loaded document are relative to it, not to the
                    // document it is inlined into, so resolve them before inlining.
                    target = InlineLocalRefs(target, document, new HashSet<string>(), depth, budget);
                    var resolved = Resolve(target, load, stack, depth + 1, budget);
                    stack.Remove(reference);
                    var siblings = new JsonObject();
                    foreach (var (key, value) in schema)
                        if (key != "$ref") siblings[key] = value?.DeepClone();
                    if (siblings.Count == 0)
                        return resolved;
                    return new JsonObject { ["allOf"] = new JsonArray(resolved, Resolve(siblings, load, stack, depth + 1, budget)) };
                }
                stack.Remove(reference);
            }
        }
        return CloneWith(schema, child => Resolve(child, load, stack, depth + 1, budget));
    }

    /// <summary>
    /// Clones <paramref name="schema"/>, replacing local <c>#...</c> references with their targets in
    /// <paramref name="document"/>. A recursive local reference is left as-is.
    /// </summary>
    private static JsonObject InlineLocalRefs(JsonObject schema, JsonObject document, HashSet<string> active, int depth, Budget budget)
    {
        budget.Spend();
        if (depth >= GtsConstants.MaxNestingDepth)
            return (JsonObject)schema.DeepClone();
        if (schema["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) &&
            reference.StartsWith('#') && !active.Contains(reference) &&
            GtsJsonPointer.TryEvaluate(document, reference, out var pointed) && pointed is JsonObject target)
        {
            active.Add(reference);
            var resolved = InlineLocalRefs(target, document, active, depth + 1, budget);
            active.Remove(reference);
            var siblings = new JsonObject();
            foreach (var (key, value) in schema)
                if (key != "$ref") siblings[key] = value?.DeepClone();
            if (siblings.Count == 0)
                return resolved;
            return new JsonObject { ["allOf"] = new JsonArray(resolved, InlineLocalRefs(siblings, document, active, depth + 1, budget)) };
        }
        return CloneWith(schema, child => InlineLocalRefs(child, document, active, depth + 1, budget));
    }

    /// <summary>Clones a schema object, mapping nested objects with <paramref name="map"/> except under literal-data keywords.</summary>
    private static JsonObject CloneWith(JsonObject schema, Func<JsonObject, JsonObject> map)
    {
        var clone = new JsonObject();
        foreach (var (key, value) in schema)
            clone[key] = LiteralKeywords.Contains(key) ? value?.DeepClone() : value switch
            {
                // Members of a schema map are schemas even when named like a literal keyword.
                JsonObject members when SchemaMapKeywords.Contains(key) => MapMembers(members, map),
                JsonObject obj => map(obj),
                JsonArray array => new JsonArray(array.Select(item => item is JsonObject child ? map(child) : item?.DeepClone()).ToArray()),
                _ => value?.DeepClone()
            };
        return clone;
    }

    private static JsonObject MapMembers(JsonObject members, Func<JsonObject, JsonObject> map)
    {
        var clone = new JsonObject();
        foreach (var (name, member) in members)
            clone[name] = member is JsonObject child ? map(child) : member?.DeepClone();
        return clone;
    }
}

/// <summary>Resolving a schema's references would exceed <see cref="GtsSchemaDependencyGraph.MaxResolvedNodes"/>.</summary>
internal sealed class GtsSchemaTooLargeException()
    : Exception($"Resolved schema exceeds {GtsSchemaDependencyGraph.MaxResolvedNodes} nodes");
