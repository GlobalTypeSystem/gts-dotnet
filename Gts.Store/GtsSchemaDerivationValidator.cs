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
            var dialect = GtsSchemaDependencyGraph.Dialect(currentSchema);
            GtsSchemaDependencyGraph.ValidateDialects(currentSchema, dialect, tryLoadTypeSchema, errors);
            ValidateReferenceTargets(currentSchema, dialect, tryLoadTypeSchema, errors, new HashSet<string>());
            if (GetParentTypeId(currentId) is not null && GtsSchemaDependencyGraph.HasCycle(currentId, currentSchema, tryLoadTypeSchema))
                errors.Add($"Schema '{currentId}' contains a cyclic GTS $ref dependency");
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
                errors.Add($"Parent GTS Type Schema not found: Precedent schema '{parentIdStr}'.");
                break;
            }

            if (parentSchema[GtsSchemaKeywords.Final] is JsonValue finalValue && finalValue.TryGetValue<bool>(out var isFinal) && isFinal)
                errors.Add($"Precedent schema '{parentIdStr}' is final and cannot be derived");
            if (GtsSchemaDependencyGraph.Dialect(parentSchema) != GtsSchemaDependencyGraph.Dialect(currentSchema))
                errors.Add($"Schema '{currentId}' uses a different JSON Schema dialect from precedent '{parentIdStr}'");
            var resolvedParent = GtsSchemaDependencyGraph.Resolve(parentSchema, tryLoadTypeSchema);
            var resolvedChild = GtsSchemaDependencyGraph.Resolve(currentSchema, tryLoadTypeSchema);
            var parentFlat = Flatten(resolvedParent);
            ValidateSubset(parentFlat, Flatten(resolvedChild), "", errors);
            ValidateRedeclarations(parentFlat, Flatten(currentSchema), "", errors);
            ValidateClosedBranches(parentFlat, currentSchema, "", errors);

            currentId = parentIdStr;
            currentSchema = parentSchema;
        }

        return (errors.Count == 0, errors);
    }

    internal static JsonObject ResolveForEvaluation(JsonObject schema, Func<GtsId, JsonObject?> loadSchema) =>
        GtsSchemaDependencyGraph.Resolve(schema, loadSchema);

    internal static IReadOnlyList<string> ValidateCompatibilityCore(JsonObject parent, JsonObject child)
    {
        var errors = new List<string>();
        var parentFlat = Flatten(parent);
        ValidateSubset(parentFlat, Flatten(child), "", errors);
        ValidateRedeclarations(parentFlat, Flatten(child), "", errors);
        ValidateClosedBranches(parentFlat, child, "", errors);
        return errors;
    }

    internal static IReadOnlyList<string> ValidateOverlayCore(JsonObject parent, JsonObject overlay)
    {
        var errors = new List<string>();
        var parentFlat = Flatten(parent);
        var overlayFlat = Flatten(overlay);
        if (overlayFlat["properties"] is JsonObject overlayPropertyMap)
        {
            foreach (var name in overlayPropertyMap.Select(property => property.Key).ToList())
            {
                if (overlayPropertyMap[name] is JsonObject property && property.All(entry => entry.Key is "default" or "title" or "description" or "examples"))
                    overlayPropertyMap.Remove(name);
            }
        }
        ValidateRedeclarations(parentFlat, overlayFlat, "", errors);
        ValidateClosedBranches(parentFlat, overlay, "", errors);
        if (IsFalse(parentFlat["additionalProperties"]) && overlay["properties"] is JsonObject overlayProperties &&
            parentFlat["properties"] is JsonObject parentProperties)
        {
            foreach (var added in overlayProperties.Select(property => property.Key).Except(parentProperties.Select(property => property.Key)))
                errors.Add($"{added}: descendant trait schema adds a field to a closed ancestor");
        }
        return errors;
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

    private static void ValidateReferenceTargets(
        JsonObject schema,
        string rootDialect,
        Func<GtsId, JsonObject?> load,
        List<string> errors,
        HashSet<string> visited)
    {
        foreach (var reference in GtsSchemaDependencyGraph.References(schema))
        {
            if (!visited.Add(reference))
                continue;
            if (!GtsId.TryParse(reference, out var parsed) || parsed is null || load(parsed) is not JsonObject target)
            {
                errors.Add($"Referenced GTS Type Schema '{reference}' not found");
                continue;
            }
            if (GtsSchemaDependencyGraph.Dialect(target) != rootDialect)
                errors.Add($"Referenced GTS Type Schema '{reference}' uses a different JSON Schema dialect");
            var keywordErrors = GtsSchemaKeywordValidator.Validate(target);
            errors.AddRange(keywordErrors.Select(error => $"Referenced GTS Type Schema '{reference}' is invalid: {error}"));
            if (parsed is not null)
            {
                var traitErrors = GtsSchemaTraitsValidator.Validate(parsed, target, load, Array.Empty<Gts.Extraction.GtsJsonEntity>(), GtsRefValidationMode.None);
                errors.AddRange(traitErrors.Select(error => $"Referenced GTS Type Schema '{reference}' is invalid: {error}"));
                var ancestorId = GetParentTypeId(reference);
                while (ancestorId is not null && GtsId.TryParse(ancestorId, out var ancestorParsed) && ancestorParsed is not null && load(ancestorParsed) is JsonObject ancestor)
                {
                    var ancestorTraitErrors = GtsSchemaTraitsValidator.Validate(ancestorParsed, ancestor, load, Array.Empty<Gts.Extraction.GtsJsonEntity>(), GtsRefValidationMode.None);
                    errors.AddRange(ancestorTraitErrors.Select(error => $"Referenced GTS Type Schema ancestor '{ancestorId}' is invalid: {error}"));
                    ancestorId = GetParentTypeId(ancestorId);
                }
            }
            if (GetParentTypeId(reference) is { } parentId && GtsId.TryParse(parentId, out var parentParsed) &&
                parentParsed is not null && load(parentParsed) is JsonObject parentSchema)
            {
                var compatibilityErrors = GtsSchemaCompatibilityService.ValidateDerivation(
                    GtsSchemaDependencyGraph.Resolve(parentSchema, load),
                    GtsSchemaDependencyGraph.Resolve(target, load));
                errors.AddRange(compatibilityErrors.Select(error => $"Referenced GTS Type Schema '{reference}' has an invalid ancestor chain: {error}"));
            }
            ValidateReferenceTargets(target, rootDialect, load, errors, visited);
        }
    }

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
        if (key == "additionalProperties")
        {
            if (IsFalse(target[key]))
                return;
            if (value is JsonValue candidate && candidate.TryGetValue<bool>(out var candidateAllowed) && candidateAllowed && target.ContainsKey(key))
                return;
            target[key] = value?.DeepClone();
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
        {
            if (parent is not JsonValue parentFalse || !parentFalse.TryGetValue<bool>(out var parentFalseAllowed) || parentFalseAllowed)
                errors.Add($"{path}: derived schema disables a property defined by the base");
            return;
        }
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

        if (IsFalse(parentObject["additionalProperties"]))
        {
            if (!IsFalse(childObject["additionalProperties"]))
                errors.Add($"{path}: derived schema reopens a closed object");
            if (childProperties is not null)
            {
                var parentNames = parentProperties?.Select(property => property.Key).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
                foreach (var added in childProperties.Select(property => property.Key).Where(name => !parentNames.Contains(name)))
                    errors.Add($"{Join(path, added)}: derived schema adds a property forbidden by a closed base");
            }
        }

        if (parentObject["items"] is JsonNode parentItems)
        {
            if (childObject["items"] is not JsonNode childItems)
                errors.Add($"{path}: derived schema drops items constraint");
            else
                ValidateSubset(parentItems, childItems, path + "[]", errors);
        }
    }

    private static void ValidateRedeclarations(JsonObject parent, JsonObject declared, string path, List<string> errors)
    {
        if (parent["properties"] is not JsonObject parentProperties || declared["properties"] is not JsonObject declaredProperties)
            return;
        foreach (var (name, childProperty) in declaredProperties)
        {
            if (parentProperties.TryGetPropertyValue(name, out var parentProperty))
            {
                if (parentProperty is JsonObject parentObject && childProperty is JsonObject childObject)
                {
                    var composed = (JsonObject)childObject.DeepClone();
                    if (parentObject["properties"] is JsonObject inheritedProperties)
                    {
                        var composedProperties = composed["properties"] as JsonObject ?? new JsonObject();
                        foreach (var (propertyName, propertySchema) in inheritedProperties)
                        {
                            if (!composedProperties.ContainsKey(propertyName))
                                composedProperties[propertyName] = propertySchema?.DeepClone();
                        }
                        composed["properties"] = composedProperties;
                    }
                    if (parentObject["required"] is JsonArray inheritedRequired)
                    {
                        var composedRequired = composed["required"] as JsonArray ?? new JsonArray();
                        var names = composedRequired.Select(item => item?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                        foreach (var requiredName in inheritedRequired.OfType<JsonValue>().Select(item => item.GetValue<string>()))
                        {
                            if (names.Add(requiredName))
                                composedRequired.Add(requiredName);
                        }
                        composed["required"] = composedRequired;
                    }
                    if (!composed.ContainsKey("additionalProperties") && parentObject["additionalProperties"] is JsonNode inheritedAdditional)
                        composed["additionalProperties"] = inheritedAdditional.DeepClone();
                    ValidateSubset(parentProperty, composed, Join(path, name), errors);
                }
                else
                    ValidateSubset(parentProperty, childProperty, Join(path, name), errors);
            }
        }
    }

    private static void ValidateClosedBranches(JsonObject ancestor, JsonObject descendant, string path, List<string> errors)
    {
        if (descendant["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf.OfType<JsonObject>())
                ValidateClosedBranches(ancestor, branch, path, errors);
        }
        if (IsFalse(descendant["additionalProperties"]) && ancestor["properties"] is JsonObject ancestorProperties)
        {
            var descendantProperties = descendant["properties"] as JsonObject;
            foreach (var name in ancestorProperties.Select(property => property.Key))
            {
                if (descendantProperties is null || !descendantProperties.ContainsKey(name))
                    errors.Add($"{Join(path, name)}: closed descendant branch does not restate an ancestor property");
            }
        }
        if (ancestor["properties"] is not JsonObject parentProperties || descendant["properties"] is not JsonObject childProperties)
            return;
        foreach (var (name, childProperty) in childProperties)
        {
            if (parentProperties[name] is JsonObject parentProperty && childProperty is JsonObject childObject)
                ValidateClosedBranches(Flatten(parentProperty), childObject, Join(path, name), errors);
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
            if (FiniteValuesMatchTypes(child, parentTypes))
                return;
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
        if (child["const"] is JsonNode childValue)
        {
            var number = Number(childValue);
            if (number is not null && (Number(parent["minimum"]) is decimal minimum && number < minimum ||
                                       Number(parent["maximum"]) is decimal maximum && number > maximum))
                errors.Add($"{path}: derived const violates a base numeric bound");
            if (parent["enum"] is JsonArray allowedValues && !allowedValues.Any(value => JsonNode.DeepEquals(value, childValue)))
                errors.Add($"{path}: derived const is not allowed by base enum");
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
        if (childValue is null && ConstraintProvenByFiniteValues(child, keyword, parentValue.Value, minimum))
            return;
        if (childValue is null || minimum && childValue < parentValue || !minimum && childValue > parentValue)
            errors.Add($"{path}: derived schema loosens {keyword}");
    }

    private static bool FiniteValuesMatchTypes(JsonObject schema, HashSet<string> types)
    {
        IEnumerable<JsonNode?> values = schema["enum"] is JsonArray array
            ? array
            : schema["const"] is JsonNode constant ? new[] { constant } : Array.Empty<JsonNode?>();
        var found = false;
        foreach (var value in values)
        {
            found = true;
            var matches = value switch
            {
                JsonValue json when json.TryGetValue<string>(out _) => types.Contains("string"),
                JsonValue json when json.TryGetValue<bool>(out _) => types.Contains("boolean"),
                JsonValue json when json.TryGetValue<long>(out _) => types.Contains("integer") || types.Contains("number"),
                JsonValue json when json.TryGetValue<double>(out _) => types.Contains("number"),
                JsonObject => types.Contains("object"),
                JsonArray => types.Contains("array"),
                null => types.Contains("null"),
                _ => false
            };
            if (!matches)
                return false;
        }
        return found;
    }

    private static bool ConstraintProvenByFiniteValues(JsonObject child, string keyword, decimal bound, bool minimum)
    {
        IEnumerable<JsonNode?> values = child["enum"] is JsonArray array
            ? array
            : child["const"] is JsonNode constant
                ? new[] { constant }
                : Array.Empty<JsonNode?>();
        var found = false;
        foreach (var value in values)
        {
            found = true;
            decimal? measured = keyword switch
            {
                "minLength" or "maxLength" when value is JsonValue stringValue && stringValue.TryGetValue<string>(out var text) => text.Length,
                "minItems" or "maxItems" when value is JsonArray itemArray => itemArray.Count,
                _ => Number(value)
            };
            if (measured is null || minimum && measured < bound || !minimum && measured > bound)
                return false;
        }
        return found;
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
