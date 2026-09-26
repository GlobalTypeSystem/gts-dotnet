using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

internal static class GtsSchemaTraitsValidator
{
    internal static IReadOnlyList<string> Validate(
        GtsId schemaId,
        JsonObject schemaDocument,
        Func<GtsId, JsonObject?> loadSchema,
        IEnumerable<GtsJsonEntity> entities,
        GtsRefValidationMode refValidationMode)
    {
        var errors = new List<string>();
        var chain = BuildChain(schemaId, schemaDocument, loadSchema, errors);
        if (errors.Count > 0)
            return errors;

        var declarations = new List<JsonNode?>();
        var values = new JsonObject();
        foreach (var schema in chain)
        {
            if (HasTraitRefCycle(schema[GtsSchemaKeywords.TraitsSchema], loadSchema, new HashSet<string>()))
                errors.Add("x-gts-traits-schema contains a cyclic GTS $ref dependency");
            CollectDeclarations(schema, schema, declarations, loadSchema);
            var levelValues = GtsTraitComposer.CollectLevelValues(schema);
            GtsTraitComposer.MergePatch(values, levelValues);
        }

        var hostDialect = NormalizeDialect(schemaDocument["$schema"]?.GetValue<string>());
        foreach (var declaration in declarations)
        {
            if (FindDialectMismatch(declaration, hostDialect))
                errors.Add("x-gts-traits-schema resource uses a different JSON Schema dialect from its host type");
            if (ContainsGtsRef(declaration))
                errors.Add("x-gts-traits-schema contains a cyclic or unresolved GTS $ref");
        }
        if (errors.Count > 0)
            return errors;

        if (declarations.Count == 0)
        {
            if (values.Count > 0)
                errors.Add("x-gts-traits values provided but no x-gts-traits-schema is defined in the inheritance chain");
            return errors;
        }

        if (declarations.Any(node => node is JsonValue value && value.TryGetValue<bool>(out var allowed) && !allowed))
        {
            if (values.Count > 0 || declarations.Any(node => node is not JsonValue))
                errors.Add("x-gts-traits-schema resolves to false and prohibits trait declarations and values");
            return errors;
        }

        foreach (var declaration in declarations)
        {
            if (declaration is not JsonObject and not JsonValue)
                errors.Add("x-gts-traits-schema must be an object subschema or boolean");
        }
        if (errors.Count > 0)
            return errors;

        var prior = new List<JsonNode?>();
        foreach (var declaration in declarations)
        {
            if (declaration is JsonObject overlay && prior.Count > 0)
            {
                var ancestor = prior.Count == 1 && prior[0] is JsonObject single
                    ? (JsonObject)single.DeepClone()
                    : new JsonObject { ["allOf"] = new JsonArray(prior.Select(node => node?.DeepClone()).ToArray()) };
                errors.AddRange(GtsSchemaCompatibilityService.ValidateTraitOverlay(ancestor, overlay)
                    .Select(error => "x-gts-traits-schema is incompatible with ancestor trait schema: " + error));
            }
            prior.Add(declaration);
        }
        if (errors.Count > 0)
            return errors;

        var effectiveSchema = GtsTraitComposer.BuildEffectiveSchema(declarations, schemaDocument["$schema"]);
        GtsTraitComposer.MaterializeDefaults(effectiveSchema, values);
        foreach (var declaration in declarations)
        {
            ValidateConstValues(declaration, values, "", errors);
            ValidateValueTypes(declaration, values, "", errors);
        }
        if (errors.Count > 0)
            return errors;

        var abstractType = schemaDocument[GtsSchemaKeywords.Abstract] is JsonValue abstractValue &&
                           abstractValue.TryGetValue<bool>(out var isAbstract) && isAbstract;
        if (abstractType)
            RemoveRequired(effectiveSchema);

        try
        {
            var result = GtsJsonSchemaEvaluator.EvaluateInline(values, effectiveSchema);
            if (!result.IsValid)
                errors.AddRange(GtsJsonSchemaEvaluator.FlattenErrors(result).Select(error => "trait validation: " + error));
            errors.AddRange(GtsRefValidator.ValidateSchema(effectiveSchema, schemaId.Id, entities, refValidationMode)
                .Select(error => "trait x-gts-ref: " + error));
            errors.AddRange(GtsRefValidator.ValidateInstance(values, effectiveSchema, schemaId.Id, entities, refValidationMode)
                .Select(error => "trait x-gts-ref: " + error));
        }
        catch (Exception exception)
        {
            errors.Add("trait schema validation failed: " + exception.Message);
        }

        return errors;
    }

    private static List<JsonObject> BuildChain(
        GtsId leafId,
        JsonObject leaf,
        Func<GtsId, JsonObject?> load,
        List<string> errors)
    {
        var chain = new List<JsonObject> { leaf };
        var current = leafId.Id;
        while (GtsSchemaDerivationValidator.GetParentTypeId(current) is { } parentId)
        {
            if (!GtsId.TryParse(parentId, out var parsed) || parsed is null || load(parsed) is not JsonObject parent)
            {
                errors.Add($"Precedent schema '{parentId}' not found");
                break;
            }
            chain.Add(parent);
            current = parentId;
        }
        chain.Reverse();
        return chain;
    }

    private static bool HasTraitRefCycle(JsonNode? node, Func<GtsId, JsonObject?> loadSchema, HashSet<string> visiting)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
            {
                var id = GtsConstants.StripUriPrefix(reference).Split('#')[0];
                if (!visiting.Add(id))
                    return true;
                if (GtsId.TryParse(id, out var parsed) && parsed is not null && loadSchema(parsed) is JsonObject target &&
                    HasTraitRefCycle(target, loadSchema, visiting))
                    return true;
                visiting.Remove(id);
            }
            return obj.Any(property => HasTraitRefCycle(property.Value, loadSchema, new HashSet<string>(visiting)));
        }
        return node is JsonArray array && array.Any(child => HasTraitRefCycle(child, loadSchema, new HashSet<string>(visiting)));
    }

    private static void CollectDeclarations(
        JsonObject schema,
        JsonObject host,
        List<JsonNode?> declarations,
        Func<GtsId, JsonObject?> loadSchema)
    {
        if (schema.TryGetPropertyValue(GtsSchemaKeywords.TraitsSchema, out var declaration))
            declarations.Add(ResolveDeclaration(declaration, host, loadSchema, new HashSet<string>(), 0));
        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf.OfType<JsonObject>())
                CollectDeclarations(branch, host, declarations, loadSchema);
        }
    }

    private static JsonNode? ResolveDeclaration(
        JsonNode? node,
        JsonObject host,
        Func<GtsId, JsonObject?> loadSchema,
        HashSet<string> visited,
        int depth)
    {
        if (depth >= GtsConstants.MaxNestingDepth)
            return node?.DeepClone();
        if (node is JsonObject obj && obj["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference))
        {
            if (reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal))
            {
                var id = GtsConstants.StripUriPrefix(reference).Split('#')[0];
                if (visited.Add(id) && GtsId.TryParse(id, out var parsed) && parsed is not null && loadSchema(parsed) is JsonObject target)
                {
                    var resolved = ResolveDeclaration(target, target, loadSchema, visited, depth + 1);
                    if (resolved is JsonObject resolvedObject)
                    {
                        resolvedObject.Remove("$id");
                        resolvedObject.Remove("$schema");
                    }
                    return resolved;
                }
            }
            else if (reference.StartsWith("#/", StringComparison.Ordinal) && GtsJsonPointer.TryEvaluate(host, reference, out var local))
                return ResolveDeclaration(local, host, loadSchema, visited, depth + 1);
        }
        if (node is JsonObject source)
        {
            var clone = new JsonObject();
            foreach (var (key, value) in source)
                clone[key] = ResolveDeclaration(value, host, loadSchema, visited, depth + 1);
            return clone;
        }
        if (node is JsonArray array)
            return new JsonArray(array.Select(value => ResolveDeclaration(value, host, loadSchema, visited, depth + 1)).ToArray());
        return node?.DeepClone();
    }


    private static void ValidateValueTypes(JsonNode? schema, JsonNode? values, string path, List<string> errors)
    {
        if (schema is not JsonObject obj)
            return;
        if (obj["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf)
                ValidateValueTypes(branch, values, path, errors);
        }
        if (obj["properties"] is not JsonObject properties || values is not JsonObject valueObject)
            return;
        foreach (var (name, propertySchema) in properties)
        {
            if (!valueObject.TryGetPropertyValue(name, out var value) || propertySchema is not JsonObject property || property["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type))
                continue;
            var matches = type switch
            {
                "object" => value is JsonObject,
                "array" => value is JsonArray,
                "string" => value is JsonValue json && json.TryGetValue<string>(out _),
                "integer" => value is JsonValue json && json.TryGetValue<long>(out _),
                "number" => value is JsonValue json && (json.TryGetValue<long>(out _) || json.TryGetValue<double>(out _)),
                "boolean" => value is JsonValue json && json.TryGetValue<bool>(out _),
                _ => true
            };
            if (!matches)
                errors.Add($"trait property '{name}' is not of type '{type}'");
            ValidateValueTypes(propertySchema, value, string.IsNullOrEmpty(path) ? name : path + "." + name, errors);
        }
    }

    private static void ValidateConstValues(JsonNode? schema, JsonNode? values, string path, List<string> errors)
    {
        if (schema is not JsonObject obj)
            return;
        if (obj["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf)
                ValidateConstValues(branch, values, path, errors);
        }
        if (obj["properties"] is not JsonObject properties || values is not JsonObject valueObject)
            return;
        foreach (var (name, propertySchema) in properties)
        {
            if (!valueObject.TryGetPropertyValue(name, out var value))
                continue;
            if (propertySchema is JsonObject property && property["const"] is JsonNode constant && !JsonNode.DeepEquals(constant, value))
                errors.Add($"trait property '{name}' violates const constraint");
            ValidateConstValues(propertySchema, value, string.IsNullOrEmpty(path) ? name : path + "." + name, errors);
        }
    }

    private static string NormalizeDialect(string? dialect)
    {
        if (dialect?.Contains("draft-07", StringComparison.Ordinal) == true)
            return "draft-07";
        if (dialect?.Contains("2019-09", StringComparison.Ordinal) == true)
            return "2019-09";
        if (dialect?.Contains("2020-12", StringComparison.Ordinal) == true)
            return "2020-12";
        return dialect?.TrimEnd('#') ?? "draft-07";
    }

    private static bool ContainsGtsRef(JsonNode? node) => node switch
    {
        JsonObject obj => obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith(GtsConstants.UriPrefix, StringComparison.Ordinal) ||
                          obj.Any(property => ContainsGtsRef(property.Value)),
        JsonArray array => array.Any(ContainsGtsRef),
        _ => false
    };

    private static bool FindDialectMismatch(JsonNode? node, string hostDialect)
    {
        if (node is JsonObject obj)
        {
            if (obj["$schema"] is JsonValue value && value.TryGetValue<string>(out var dialect) && NormalizeDialect(dialect) != hostDialect)
                return true;
            return obj.Any(property => FindDialectMismatch(property.Value, hostDialect));
        }
        return node is JsonArray array && array.Any(child => FindDialectMismatch(child, hostDialect));
    }

    private static void RemoveRequired(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return;
        obj.Remove("required");
        foreach (var keyword in new[] { "properties", "patternProperties", "definitions", "$defs", "dependentSchemas" })
        {
            if (obj[keyword] is JsonObject map)
            {
                foreach (var child in map.Select(property => property.Value))
                    RemoveRequired(child);
            }
        }
        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
        {
            if (obj[keyword] is JsonArray array)
            {
                foreach (var child in array)
                    RemoveRequired(child);
            }
        }
        foreach (var keyword in new[] { "items", "additionalItems", "additionalProperties", "contains", "not", "if", "then", "else" })
            RemoveRequired(obj[keyword]);
    }
}
