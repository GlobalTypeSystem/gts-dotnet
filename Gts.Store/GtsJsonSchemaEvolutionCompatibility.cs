using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>
/// JSON Schema minor-evolution compatibility checks (backward / forward) aligned with the GTS reference implementation.
/// </summary>
public static class GtsJsonSchemaEvolutionCompatibility
{
    /// <summary>Backward: instances valid under <paramref name="oldSchema"/> remain valid under <paramref name="newSchema"/>.</summary>
    public static (bool Ok, IReadOnlyList<string> Errors) CheckBackward(JsonObject oldSchema, JsonObject newSchema)
        => CheckSchemaCompatibility(oldSchema, newSchema, checkBackward: true);

    /// <summary>Forward: instances valid under <paramref name="newSchema"/> remain valid under <paramref name="oldSchema"/>.</summary>
    public static (bool Ok, IReadOnlyList<string> Errors) CheckForward(JsonObject oldSchema, JsonObject newSchema)
        => CheckSchemaCompatibility(oldSchema, newSchema, checkBackward: false);

    private static (bool Ok, IReadOnlyList<string> Errors) CheckSchemaCompatibility(
        JsonObject oldSchema,
        JsonObject newSchema,
        bool checkBackward)
    {
        var errors = new List<string>();
        var oldFlat = FlattenSchema(oldSchema);
        var newFlat = FlattenSchema(newSchema);

        var oldProps = GetProperties(oldFlat);
        var newProps = GetProperties(newFlat);
        var oldReq = GetRequired(oldFlat);
        var newReq = GetRequired(newFlat);

        if (checkBackward)
        {
            var newlyRequired = newReq.Except(oldReq).ToHashSet();
            if (newlyRequired.Count > 0)
                errors.Add($"Added required properties: {string.Join(", ", newlyRequired)}");
        }
        else
        {
            var removedRequired = oldReq.Except(newReq).ToHashSet();
            if (removedRequired.Count > 0)
                errors.Add($"Removed required properties: {string.Join(", ", removedRequired)}");
        }

        foreach (var prop in oldProps.Keys.Intersect(newProps.Keys))
        {
            var oldPropSchema = AsObject(oldProps[prop]);
            var newPropSchema = AsObject(newProps[prop]);
            if (oldPropSchema is null || newPropSchema is null)
                continue;

            var oldType = GetTypeString(oldPropSchema);
            var newType = GetTypeString(newPropSchema);

            if (oldType is not null && newType is not null && oldType != newType)
                errors.Add($"Property '{prop}' type changed from {oldType} to {newType}");

            var oldEnum = oldPropSchema["enum"] as JsonArray;
            var newEnum = newPropSchema["enum"] as JsonArray;
            if (oldEnum is not null && newEnum is not null)
            {
                var oldSet = EnumToStringSet(oldEnum);
                var newSet = EnumToStringSet(newEnum);
                if (checkBackward)
                {
                    var added = newSet.Except(oldSet).ToHashSet();
                    if (added.Count > 0)
                        errors.Add($"Property '{prop}' added enum values: {string.Join(", ", added)}");
                }
                else
                {
                    var removed = oldSet.Except(newSet).ToHashSet();
                    if (removed.Count > 0)
                        errors.Add($"Property '{prop}' removed enum values: {string.Join(", ", removed)}");
                }
            }

            errors.AddRange(CheckConstraintCompatibility(prop, oldPropSchema, newPropSchema, checkBackward));

            if (oldType == "object" && newType == "object")
            {
                var (nestedOk, nestedErrs) = CheckSchemaCompatibility(oldPropSchema, newPropSchema, checkBackward);
                if (!nestedOk)
                {
                    foreach (var err in nestedErrs)
                        errors.Add($"Property '{prop}': {err}");
                }
            }

            if (oldType == "array" && newType == "array")
            {
                var oi = oldPropSchema["items"] as JsonObject;
                var ni = newPropSchema["items"] as JsonObject;
                if (oi is not null && ni is not null)
                {
                    var oit = GetTypeString(oi);
                    var nit = GetTypeString(ni);
                    if (oit == "object" && nit == "object")
                    {
                        var (nestedOk, nestedErrs) = CheckSchemaCompatibility(oi, ni, checkBackward);
                        if (!nestedOk)
                        {
                            foreach (var err in nestedErrs)
                                errors.Add($"Property '{prop}': {err}");
                        }
                    }
                }
            }
        }

        return (errors.Count == 0, errors);
    }

    private static HashSet<string> EnumToStringSet(JsonArray arr)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in arr)
        {
            if (n is JsonValue v && v.TryGetValue<string>(out var s))
                set.Add(s);
        }

        return set;
    }

    private static string? GetTypeString(JsonObject propSchema)
    {
        if (propSchema.TryGetPropertyValue("type", out var t))
        {
            if (t is JsonValue jv && jv.TryGetValue<string>(out var s))
                return s;
        }

        return null;
    }

    private static JsonObject? AsObject(JsonNode? n) => n as JsonObject;

    private static IReadOnlyList<string> CheckConstraintCompatibility(
        string prop,
        JsonObject oldPropSchema,
        JsonObject newPropSchema,
        bool checkTightening)
    {
        var errors = new List<string>();
        var propType = GetTypeString(oldPropSchema);

        if (propType is "number" or "integer")
            errors.AddRange(CheckMinMaxConstraint(prop, oldPropSchema, newPropSchema, "minimum", "maximum", checkTightening));

        if (propType == "string")
            errors.AddRange(CheckMinMaxConstraint(prop, oldPropSchema, newPropSchema, "minLength", "maxLength", checkTightening));

        if (propType == "array")
            errors.AddRange(CheckMinMaxConstraint(prop, oldPropSchema, newPropSchema, "minItems", "maxItems", checkTightening));

        return errors;
    }

    private static IReadOnlyList<string> CheckMinMaxConstraint(
        string prop,
        JsonObject oldSchema,
        JsonObject newSchema,
        string minKey,
        string maxKey,
        bool checkTightening)
    {
        var errors = new List<string>();

        var oldMin = GetNumber(oldSchema, minKey);
        var newMin = GetNumber(newSchema, minKey);
        if (oldMin is not null && newMin is not null)
        {
            if (checkTightening && newMin > oldMin)
                errors.Add($"Property '{prop}' {minKey} increased from {oldMin} to {newMin}");
            if (!checkTightening && newMin < oldMin)
                errors.Add($"Property '{prop}' {minKey} decreased from {oldMin} to {newMin}");
        }
        else if (checkTightening && oldMin is null && newMin is not null)
            errors.Add($"Property '{prop}' added {minKey} constraint: {newMin}");
        else if (!checkTightening && oldMin is not null && newMin is null)
            errors.Add($"Property '{prop}' removed {minKey} constraint");

        var oldMax = GetNumber(oldSchema, maxKey);
        var newMax = GetNumber(newSchema, maxKey);
        if (oldMax is not null && newMax is not null)
        {
            if (checkTightening && newMax < oldMax)
                errors.Add($"Property '{prop}' {maxKey} decreased from {oldMax} to {newMax}");
            if (!checkTightening && newMax > oldMax)
                errors.Add($"Property '{prop}' {maxKey} increased from {oldMax} to {newMax}");
        }
        else if (checkTightening && oldMax is null && newMax is not null)
            errors.Add($"Property '{prop}' added {maxKey} constraint: {newMax}");
        else if (!checkTightening && oldMax is not null && newMax is null)
            errors.Add($"Property '{prop}' removed {maxKey} constraint");

        return errors;
    }

    private static decimal? GetNumber(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue jv)
            return null;
        if (jv.TryGetValue(out long l))
            return l;
        if (jv.TryGetValue(out double d))
            return (decimal)d;
        if (jv.TryGetValue(out decimal m))
            return m;
        return null;
    }

    private static Dictionary<string, JsonNode?> GetProperties(JsonObject flat)
    {
        var d = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (flat.TryGetPropertyValue("properties", out var p) && p is JsonObject po)
        {
            foreach (var (k, v) in po)
                d[k] = v;
        }

        return d;
    }

    private static HashSet<string> GetRequired(JsonObject flat)
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        if (flat.TryGetPropertyValue("required", out var r) && r is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonValue jv && jv.TryGetValue<string>(out var name))
                    s.Add(name);
            }
        }

        return s;
    }

    /// <summary>Merges <c>allOf</c> fragments into a single object shape (properties/required/additionalProperties).</summary>
    public static JsonObject FlattenSchema(JsonObject schema)
    {
        var result = new JsonObject
        {
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray()
        };

        if (schema.TryGetPropertyValue("allOf", out var allof) && allof is JsonArray parts)
        {
            foreach (var part in parts)
            {
                if (part is not JsonObject sub)
                    continue;
                var flattened = FlattenSchema(sub);
                MergeFlatInto(result, flattened);
            }
        }

        if (schema.TryGetPropertyValue("properties", out var props) && props is JsonObject po)
        {
            var targetProps = (JsonObject)result["properties"]!;
            foreach (var (k, v) in po)
                targetProps[k] = v?.DeepClone();
        }

        if (schema.TryGetPropertyValue("required", out var req) && req is JsonArray rq)
        {
            var targetReq = (JsonArray)result["required"]!;
            foreach (var item in rq)
                targetReq.Add(item?.DeepClone());
        }

        if (schema.TryGetPropertyValue("additionalProperties", out var ap))
            result["additionalProperties"] = ap.DeepClone();

        return result;
    }

    private static void MergeFlatInto(JsonObject target, JsonObject flattened)
    {
        if (flattened.TryGetPropertyValue("properties", out var fp) && fp is JsonObject fpo)
        {
            var tp = (JsonObject)target["properties"]!;
            foreach (var (k, v) in fpo)
                tp[k] = v?.DeepClone();
        }

        if (flattened.TryGetPropertyValue("required", out var fr) && fr is JsonArray fra)
        {
            var tr = (JsonArray)target["required"]!;
            foreach (var item in fra)
                tr.Add(item?.DeepClone());
        }

        if (flattened.TryGetPropertyValue("additionalProperties", out var ap))
            target["additionalProperties"] = ap.DeepClone();
    }
}
