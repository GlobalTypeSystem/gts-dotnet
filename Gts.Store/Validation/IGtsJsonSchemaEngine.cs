using System.Text.Json.Nodes;
using Json.Schema;

namespace Gts.Store.Validation;

internal interface IGtsJsonSchemaEngine
{
    EvaluationResults Evaluate(JsonNode? instance, GtsId rootSchemaId, IReadOnlyDictionary<GtsId, JsonObject> schemas);

    EvaluationResults EvaluateInline(JsonNode? instance, JsonObject schemaDocument);

    IReadOnlyList<string> FlattenErrors(EvaluationResults results);
}