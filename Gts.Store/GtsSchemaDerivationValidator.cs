using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Validates that a derived JSON Schema remains forward-compatible with each precedent (parent type) in the GTS type id chain.</summary>
public static class GtsSchemaDerivationValidator
{
    /// <summary>Walks the type-id chain toward the base and checks forward compatibility against each precedent schema for each hop.</summary>
    public static (bool Ok, IReadOnlyList<string> Errors) ValidateAgainstRegistry(
        GtsId derivedId,
        JsonObject derivedSchema,
        Func<GtsId, JsonObject?> tryLoadTypeSchema)
    {
        var errors = new List<string>();
        var currentId = derivedId.Id;
        var currentSchema = derivedSchema;

        while (true)
        {
            var parentIdStr = GetParentTypeId(currentId);
            if (parentIdStr is null)
                break;

            if (!GtsId.TryParse(parentIdStr, out var parentId) || parentId is null)
            {
                errors.Add($"Invalid precedent type id '{parentIdStr}'.");
                break;
            }

            var parentSchema = tryLoadTypeSchema(parentId);
            if (parentSchema is null)
            {
                errors.Add($"Precedent schema '{parentIdStr}' not found.");
                break;
            }

            var resolvedParent = ResolveRefs(parentSchema, tryLoadTypeSchema, new HashSet<string>());
            var resolvedChild = ResolveRefs(currentSchema, tryLoadTypeSchema, new HashSet<string>());
            ValidateSubset(Flatten(resolvedParent), Flatten(resolvedChild), "", errors);

            currentId = parentIdStr;
            currentSchema = parentSchema;
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>Strips the last <c>~segment</c> from a chained type id, or returns null for a single-segment base.</summary>
    public static string? GetParentTypeId(string schemaTypeId)
    {
        if (string.IsNullOrEmpty(schemaTypeId) || !schemaTypeId.EndsWith("~", StringComparison.Ordinal))
            return null;

        var withoutTrailing = schemaTypeId.AsSpan(0, schemaTypeId.Length - 1);
        var last = withoutTrailing.LastIndexOf('~');
        if (last < 0)
            return null;

        return withoutTrailing[..(last + 1)].ToString();
    }

    private static JsonObject ResolveRefs(JsonObject schema, Func<GtsId, JsonObject?> load, HashSet<string> stack)
    {
        if (schema["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) &&
            reference.StartsWith("gts://", StringComparison.Ordinal))
        {
            var id = reference[6..];
            if (GtsId.TryParse(id, out var parsed) && parsed is not null && stack.Add(id))
            {
                var target = load(parsed);
                if (target is not null)
                {
                    var resolved = ResolveRefs(target, load, stack);
                    stack.Remove(id);
                    return resolved;
                }
            }
        }

        var clone = new JsonObject();
        foreach (var (key, value) in schema)
            clone[key] = ResolveNode(value, load, stack);
        return clone;
    }

    private static JsonNode? ResolveNode(JsonNode? node, Func<GtsId, JsonObject?> load, HashSet<string> stack) => node switch
    {
        JsonObject obj => ResolveRefs(obj, load, stack),
        JsonArray array => new JsonArray(array.Select(item => ResolveNode(item, load, stack)).ToArray()),
        _ => node?.DeepClone()
    };

    private static JsonObject Flatten(JsonObject schema)
    {
        var result = new JsonObject();
        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf.OfType<JsonObject>())
                Merge(result, Flatten(branch));
        }
        foreach (var (key, value) in schema)
        {
            if (key != "allOf")
                MergeKeyword(result, key, value);
        }
        return result;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
            MergeKeyword(target, key, value);
    }

    private static void MergeKeyword(JsonObject target, string key, JsonNode? value)
    {
        if (key == "properties" && value is JsonObject properties)
        {
            var targetProperties = target["properties"] as JsonObject ?? new JsonObject();
            foreach (var (name, property) in properties)
            {
                if (targetProperties[name] is JsonObject inherited && property is JsonObject overlay)
                {
                    var merged = Flatten(inherited);
                    Merge(merged, Flatten(overlay));
                    targetProperties[name] = merged;
                }
                else
                    targetProperties[name] = property?.DeepClone();
            }
            target["properties"] = targetProperties;
            return;
        }
        if (key == "required" && value is JsonArray required)
        {
            var targetRequired = target["required"] as JsonArray ?? new JsonArray();
            var names = targetRequired.Select(item => item?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            foreach (var item in required)
            {
                var name = item?.GetValue<string>();
                if (name is not null && names.Add(name))
                    targetRequired.Add(name);
            }
            target["required"] = targetRequired;
            return;
        }
        target[key] = value?.DeepClone();
    }

    private static void ValidateSubset(JsonNode? parent, JsonNode? child, string path, List<string> errors)
    {
        if (parent is JsonValue parentBool && parentBool.TryGetValue<bool>(out var parentAllowed))
        {
            if (!parentAllowed && (child is not JsonValue childBool || !childBool.TryGetValue<bool>(out var childAllowed) || childAllowed))
                errors.Add($"{path}: derived schema enables values rejected by base");
            return;
        }
        if (child is JsonValue childFalse && childFalse.TryGetValue<bool>(out var allowed) && !allowed)
            return;
        if (parent is not JsonObject parentObject || child is not JsonObject childObject)
            return;

        ValidateTypes(parentObject, childObject, path, errors);
        ValidateEnumAndConst(parentObject, childObject, path, errors);
        ValidateBounds(parentObject, childObject, path, errors);

        if (parentObject["pattern"] is JsonValue parentPattern && parentPattern.TryGetValue<string>(out var pattern) &&
            (childObject["pattern"] is not JsonValue childPattern || !childPattern.TryGetValue<string>(out var childPatternValue) || childPatternValue != pattern))
            errors.Add($"{path}: derived schema drops or changes pattern");

        var parentRequired = Names(parentObject["required"] as JsonArray);
        var childRequired = Names(childObject["required"] as JsonArray);
        foreach (var missing in parentRequired.Except(childRequired))
            errors.Add($"{Join(path, missing)}: derived schema drops a required property");

        var parentProperties = parentObject["properties"] as JsonObject;
        var childProperties = childObject["properties"] as JsonObject;
        if (parentProperties is not null)
        {
            foreach (var (name, parentProperty) in parentProperties)
            {
                if (childProperties is null || !childProperties.TryGetPropertyValue(name, out var childProperty))
                {
                    errors.Add($"{Join(path, name)}: derived schema drops a base property");
                    continue;
                }
                ValidateSubset(parentProperty, childProperty, Join(path, name), errors);
            }
        }

        if (IsFalse(parentObject["additionalProperties"]) && !IsFalse(childObject["additionalProperties"]))
            errors.Add($"{path}: derived schema reopens a closed object");

        if (parentObject["items"] is JsonNode parentItems)
        {
            if (childObject["items"] is not JsonNode childItems)
                errors.Add($"{path}: derived schema drops items constraint");
            else
                ValidateSubset(parentItems, childItems, path + "[]", errors);
        }
    }

    private static void ValidateTypes(JsonObject parent, JsonObject child, string path, List<string> errors)
    {
        var parentTypes = Types(parent["type"]);
        var childTypes = Types(child["type"]);
        if (parentTypes.Count == 0)
            return;
        if (childTypes.Count == 0)
        {
            errors.Add($"{path}: derived schema drops type constraint");
            return;
        }
        foreach (var type in childTypes)
        {
            if (!parentTypes.Contains(type) && !(type == "integer" && parentTypes.Contains("number")))
                errors.Add($"{path}: derived type '{type}' is not included in base type");
        }
    }

    private static void ValidateEnumAndConst(JsonObject parent, JsonObject child, string path, List<string> errors)
    {
        if (parent["const"] is JsonNode parentConst)
        {
            if (child["const"] is not JsonNode childConst || !JsonNode.DeepEquals(parentConst, childConst))
                errors.Add($"{path}: derived schema drops or changes const");
        }
        if (parent["enum"] is JsonArray parentEnum)
        {
            if (child["enum"] is not JsonArray childEnum)
            {
                errors.Add($"{path}: derived schema drops enum");
                return;
            }
            foreach (var value in childEnum)
            {
                if (!parentEnum.Any(parentValue => JsonNode.DeepEquals(parentValue, value)))
                    errors.Add($"{path}: derived enum adds a value rejected by base");
            }
        }
    }

    private static void ValidateBounds(JsonObject parent, JsonObject child, string path, List<string> errors)
    {
        foreach (var keyword in new[] { "minimum", "exclusiveMinimum", "minLength", "minItems", "minProperties" })
            ValidateBound(parent, child, path, errors, keyword, minimum: true);
        foreach (var keyword in new[] { "maximum", "exclusiveMaximum", "maxLength", "maxItems", "maxProperties" })
            ValidateBound(parent, child, path, errors, keyword, minimum: false);
    }

    private static void ValidateBound(JsonObject parent, JsonObject child, string path, List<string> errors, string keyword, bool minimum)
    {
        var parentValue = Number(parent[keyword]);
        if (parentValue is null)
            return;
        var childValue = Number(child[keyword]);
        if (childValue is null || minimum && childValue < parentValue || !minimum && childValue > parentValue)
            errors.Add($"{path}: derived schema loosens {keyword}");
    }

    private static HashSet<string> Types(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var type))
            return new HashSet<string>(new[] { type }, StringComparer.Ordinal);
        if (node is JsonArray array)
            return array.OfType<JsonValue>().Select(item => item.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        return new HashSet<string>(StringComparer.Ordinal);
    }

    private static HashSet<string> Names(JsonArray? array) => array is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : array.OfType<JsonValue>().Select(item => item.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

    private static decimal? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<decimal>(out var number))
            return number;
        if (value.TryGetValue<double>(out var doubleNumber))
            return (decimal)doubleNumber;
        if (value.TryGetValue<long>(out var integer))
            return integer;
        return null;
    }

    private static bool IsFalse(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var result) && !result;

    private static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;
}
