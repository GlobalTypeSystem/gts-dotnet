using System.Text.Json.Nodes;
using Json.Schema;

namespace Gts.Store.Validation;

internal static class GtsJsonSchemaEvaluator
{
    private static readonly IGtsJsonSchemaEngine Engine = GtsJsonSchemaEngine.Default;

    internal static EvaluationResults Evaluate(
        JsonNode? instance,
        GtsId rootSchemaId,
        IReadOnlyDictionary<GtsId, JsonObject> normalizedSchemasById) =>
        Engine.Evaluate(instance, rootSchemaId, normalizedSchemasById);

    internal static EvaluationResults EvaluateInline(JsonNode? instance, JsonObject schemaDocument) =>
        Engine.EvaluateInline(instance, schemaDocument);

    internal static IReadOnlyList<string> FlattenErrors(EvaluationResults results) => Engine.FlattenErrors(results);
}