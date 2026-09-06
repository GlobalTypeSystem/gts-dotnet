using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Gts.Store.Validation;

internal static class GtsJsonSchemaEvaluator
{
    internal static EvaluationResults Evaluate(
        JsonElement instance,
        GtsId rootSchemaId,
        IReadOnlyDictionary<GtsId, JsonObject> normalizedSchemasById)
    {
        var registry = new SchemaRegistry();
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft07,
            SchemaRegistry = registry
        };

        registry.Fetch = (uri, _) =>
        {
            if (!GtsSchemaResolutionUris.TryGetGtsId(uri, out var idStr))
                return null;
            if (!GtsId.TryParse(idStr, out var gid) || gid is null)
                return null;
            if (!normalizedSchemasById.TryGetValue(gid, out var doc))
                return null;
            return JsonSchema.FromText(doc.ToJsonString(), buildOptions, uri);
        };

        var rootUri = GtsSchemaResolutionUris.ToSyntheticUri(rootSchemaId.Id);
        if (!normalizedSchemasById.TryGetValue(rootSchemaId, out var rootDoc))
            throw new InvalidOperationException("Root schema is missing from the normalized map.");

        var schema = JsonSchema.FromText(rootDoc.ToJsonString(), buildOptions, rootUri);

        var evalOptions = new EvaluationOptions
        {
            RequireFormatValidation = true,
            OutputFormat = OutputFormat.List
        };

        return schema.Evaluate(instance, evalOptions);
    }

    internal static IReadOnlyList<string> FlattenErrors(EvaluationResults results)
    {
        var list = new List<string>();
        Walk(results, list);
        return list;
    }

    private static void Walk(EvaluationResults node, List<string> sink)
    {
        if (!node.IsValid)
        {
            if (node.Errors is { Count: > 0 })
            {
                foreach (var err in node.Errors)
                    sink.Add($"{node.InstanceLocation}: {err}");
            }
            else if (node.Details is not { Count: > 0 })
                sink.Add($"{node.InstanceLocation} @ {node.EvaluationPath}");
        }

        foreach (var d in node.Details ?? [])
            Walk(d, sink);
    }
}
