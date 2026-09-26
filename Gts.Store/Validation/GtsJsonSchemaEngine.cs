using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Gts.Store.Validation;

internal sealed class GtsJsonSchemaEngine : IGtsJsonSchemaEngine
{
    internal static GtsJsonSchemaEngine Default { get; } = new();

    private readonly ConcurrentDictionary<string, JsonSchema> _compiled = new(StringComparer.Ordinal);
    private readonly FormatRegistry _formats = GtsFormatRegistry.Create();

    public EvaluationResults Evaluate(JsonNode? instance, GtsId rootSchemaId, IReadOnlyDictionary<GtsId, JsonObject> schemas)
    {
        if (!schemas.TryGetValue(rootSchemaId, out var root))
            throw new InvalidOperationException("Root schema is missing from the schema map.");

        // Fingerprint only the schemas actually reachable from the root via $ref, not the entire
        // registry. This keeps the cache key (and the work to compute it) proportional to the schema
        // graph being compiled instead of the store size, and means an unrelated schema change no
        // longer needlessly invalidates this entry — any change that alters reachability is itself a
        // change to a reachable document, so the key still moves when it must.
        var reachable = ReachableClosure(rootSchemaId, root, schemas);
        var fingerprint = Fingerprint(rootSchemaId.Id, reachable.Select(item => item.Value));
        // Compile against only the reachable closure, not the whole registry: each cached
        // JsonSchema keeps its SchemaRegistry (and the dictionary captured by registry.Fetch)
        // alive, so capturing the full store would retain O(cache size × store size) of JSON.
        var closure = reachable.ToDictionary(item => item.Key, item => item.Value);
        var schema = GetOrCompile(fingerprint, () => Compile(rootSchemaId, root, closure));
        return schema.Evaluate(GtsJson.ToElement(instance), EvaluationOptions());
    }

    public EvaluationResults EvaluateInline(JsonNode? instance, JsonObject schemaDocument)
    {
        var normalized = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(schemaDocument);
        var fingerprint = Fingerprint("inline", new[] { normalized });
        var schema = GetOrCompile(fingerprint, () => JsonSchema.FromText(normalized.ToJsonString(), BuildOptions(normalized, new SchemaRegistry())));
        return schema.Evaluate(GtsJson.ToElement(instance), EvaluationOptions());
    }

    public IReadOnlyList<string> FlattenErrors(EvaluationResults results)
    {
        var errors = new List<string>();
        Walk(results, errors);
        return errors;
    }

    private JsonSchema GetOrCompile(string fingerprint, Func<JsonSchema> compile)
    {
        if (_compiled.Count >= 512)
            _compiled.Clear();
        return _compiled.GetOrAdd(fingerprint, _ => compile());
    }

    private JsonSchema Compile(GtsId rootId, JsonObject root, IReadOnlyDictionary<GtsId, JsonObject> schemas)
    {
        var registry = new SchemaRegistry();
        var options = BuildOptions(root, registry);
        registry.Fetch = (uri, _) =>
        {
            if (!GtsSchemaResolutionUris.TryGetGtsId(uri, out var id) || !GtsId.TryParse(id, out var parsed) || parsed is null || !schemas.TryGetValue(parsed, out var document))
                return null;
            return JsonSchema.FromText(document.ToJsonString(), options, uri);
        };
        return JsonSchema.FromText(root.ToJsonString(), options, GtsSchemaResolutionUris.ToSyntheticUri(rootId.Id));
    }

    private static BuildOptions BuildOptions(JsonObject schema, SchemaRegistry registry) => new()
    {
        Dialect = DialectFor(schema),
        SchemaRegistry = registry,
        DialectRegistry = new DialectRegistry(),
        VocabularyRegistry = new VocabularyRegistry()
    };

    private EvaluationOptions EvaluationOptions() => new()
    {
        RequireFormatValidation = true,
        OutputFormat = OutputFormat.List,
        FormatRegistry = _formats
    };

    private static Dialect DialectFor(JsonObject schema)
    {
        var uri = schema["$schema"] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        if (uri?.Contains("2020-12", StringComparison.Ordinal) == true)
            return Dialect.Draft202012;
        if (uri?.Contains("2019-09", StringComparison.Ordinal) == true)
            return Dialect.Draft201909;
        return Dialect.Draft07;
    }

    private static string Fingerprint(string root, IEnumerable<JsonObject> schemas)
    {
        var builder = new StringBuilder(root);
        foreach (var schema in schemas)
            builder.Append('\n').Append(schema.ToJsonString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Returns the root schema plus every schema transitively reachable from it via <c>$ref</c>,
    /// ordered by id for a stable fingerprint. Cycle-safe (each id is visited once).
    /// </summary>
    private static List<KeyValuePair<GtsId, JsonObject>> ReachableClosure(
        GtsId rootId, JsonObject root, IReadOnlyDictionary<GtsId, JsonObject> schemas)
    {
        var reachable = new List<KeyValuePair<GtsId, JsonObject>> { new(rootId, root) };
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId.Id };
        var queue = new Queue<JsonObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            foreach (var refUri in CollectRefUris(queue.Dequeue(), 0))
            {
                var basePart = refUri.Split('#')[0];
                if (!Uri.TryCreate(basePart, UriKind.Absolute, out var uri) ||
                    !GtsSchemaResolutionUris.TryGetGtsId(uri, out var id) ||
                    !GtsId.TryParse(id, out var parsed) || parsed is null ||
                    !visited.Add(parsed.Id) ||
                    !schemas.TryGetValue(parsed, out var target))
                    continue;
                reachable.Add(new(parsed, target));
                queue.Enqueue(target);
            }
        }
        reachable.Sort((a, b) => string.CompareOrdinal(a.Key.Id, b.Key.Id));
        return reachable;
    }

    private static IEnumerable<string> CollectRefUris(JsonNode? node, int depth)
    {
        if (depth >= GtsConstants.MaxNestingDepth)
            yield break;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key == "$ref" && value is JsonValue rv && rv.TryGetValue<string>(out var reference))
                        yield return reference;
                    else
                        foreach (var nested in CollectRefUris(value, depth + 1))
                            yield return nested;
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                    foreach (var nested in CollectRefUris(child, depth + 1))
                        yield return nested;
                break;
        }
    }

    private static void Walk(EvaluationResults node, List<string> errors)
    {
        if (!node.IsValid)
        {
            if (node.Errors is { Count: > 0 })
            {
                foreach (var error in node.Errors)
                    errors.Add($"{node.InstanceLocation}: {error}");
            }
            else if (node.Details is not { Count: > 0 })
                errors.Add($"{node.InstanceLocation} @ {node.EvaluationPath}");
        }
        foreach (var detail in node.Details ?? [])
            Walk(detail, errors);
    }
}