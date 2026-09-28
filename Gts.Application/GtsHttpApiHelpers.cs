using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gts.Store;

namespace Gts.Application;

public static partial class GtsHttpApiExtensions
{
    internal static int EnumConstraintChange(JsonObject oldSchema, JsonObject newSchema)
    {
        var oldHasEnum = ContainsSchemaKeyword(oldSchema, "enum");
        var newHasEnum = ContainsSchemaKeyword(newSchema, "enum");
        if (oldHasEnum && !newHasEnum) return -1;
        if (!oldHasEnum && newHasEnum) return 1;
        return 0;
    }

    internal static string NormalizeDialect(string? dialect)
    {
        if (dialect?.Contains("draft-07", StringComparison.Ordinal) == true) return "draft-07";
        if (dialect?.Contains("2019-09", StringComparison.Ordinal) == true) return "2019-09";
        if (dialect?.Contains("2020-12", StringComparison.Ordinal) == true) return "2020-12";
        return dialect?.TrimEnd('#') ?? "draft-07";
    }

    internal static bool ContainsSchemaKeyword(JsonNode? node, string keyword) => node switch
    {
        JsonObject obj => obj.ContainsKey(keyword) || obj.Any(property => ContainsSchemaKeyword(property.Value, keyword)),
        JsonArray array => array.Any(item => ContainsSchemaKeyword(item, keyword)),
        _ => false
    };

    internal static JsonObject ResolveSchemaRefs(JsonObject schema, IReadOnlyDictionary<string, JsonObject> schemas, HashSet<string> stack)
    {
        if (schema["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
        {
            var id = GtsConstants.StripUriPrefix(reference).Split('#')[0];
            if (schemas.TryGetValue(id, out var target) && stack.Add(id))
            {
                var resolved = ResolveSchemaRefs(target, schemas, stack);
                stack.Remove(id);
                return resolved;
            }
        }
        var clone = new JsonObject();
        foreach (var (key, value) in schema)
        {
            clone[key] = value switch
            {
                JsonObject child => ResolveSchemaRefs(child, schemas, stack),
                JsonArray array => new JsonArray(array.Select(item => item is JsonObject child ? ResolveSchemaRefs(child, schemas, stack) : item?.DeepClone()).ToArray()),
                _ => value?.DeepClone()
            };
        }
        return clone;
    }

    internal static GtsRefValidationMode NormalizeRefValidationMode(string? value) => GtsRefValidationModes.Parse(value);

    internal static string? InstanceError(GtsInstanceValidationResult result)
    {
        if (result.Ok) return null;
        if (result.SchemaErrors is { Count: > 0 })
            return Regex.Replace(string.Join("; ", result.SchemaErrors), "Value is \\\"[^\\\"]+\\\" but should be \\\"([^\\\"]+)\\\"", "is not of type '$1'");
        return result.FailureReason switch
        {
            GtsValidationFailure.SchemaNotFound => "GTS Type Schema not found",
            GtsValidationFailure.NotASchema => "Registered entity must be GTS Type schema",
            _ => result.FailureReason?.ToWire() ?? "Validation failed"
        };
    }

    internal static string? SchemaError(GtsSchemaValidationResult result)
    {
        if (result.Ok) return null;
        if (result.Errors is { Count: > 0 }) return string.Join("; ", result.Errors);
        return result.FailureReason == GtsValidationFailure.PrecedentIncompatible ? "Parent GTS Type Schema not found" : result.FailureReason?.ToWire() ?? "JSON Schema validation failed";
    }
}