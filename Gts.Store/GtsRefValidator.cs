using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

public static class GtsRefValidator
{
    public static IReadOnlyList<string> ValidatePatterns(JsonObject schema, string selectedTypeId) =>
        ValidateSchema(schema, selectedTypeId, Array.Empty<GtsJsonEntity>(), GtsRefValidationMode.None);

    public static IReadOnlyList<string> ValidateConstraints(JsonObject schema, string selectedTypeId, IEnumerable<GtsJsonEntity> entities, GtsRefValidationMode mode) =>
        ValidateSchema(schema, selectedTypeId, entities, mode);

    internal static IReadOnlyList<string> ValidateSchema(
        JsonObject schema,
        string selectedTypeId,
        IEnumerable<GtsJsonEntity> entities,
        GtsRefValidationMode mode)
    {
        var errors = new List<string>();
        WalkSchema(schema, schema, selectedTypeId, RefContext.Create(entities), mode, errors, 0);
        return errors;
    }

    internal static IReadOnlyList<string> ValidateInstance(
        JsonObject instance,
        JsonObject schema,
        string selectedTypeId,
        IEnumerable<GtsJsonEntity> entities,
        GtsRefValidationMode mode)
    {
        var errors = new List<string>();
        WalkInstance(instance, schema, schema, selectedTypeId,
            RefContext.Create(entities), mode, "", errors, new HashSet<string>(), 0);
        return errors;
    }

    private static void WalkSchema(
        JsonNode? node,
        JsonObject root,
        string selectedTypeId,
        RefContext context,
        GtsRefValidationMode mode,
        List<string> errors,
        int depth)
    {
        if (depth >= GtsConstants.MaxNestingDepth)
            return;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(GtsSchemaKeywords.Ref, out var refNode))
            {
                if (refNode is not JsonValue value || !value.TryGetValue<string>(out var pattern))
                    errors.Add("x-gts-ref value must be a string");
                else if (!TryResolvePattern(pattern, selectedTypeId, out var resolved))
                    errors.Add(pattern.StartsWith(GtsConstants.IdPrefix, StringComparison.Ordinal) && !pattern.Contains('*')
                        ? $"Invalid GTS identifier: {pattern}"
                        : $"Invalid x-gts-ref value: '{pattern}'");
                else if (mode != GtsRefValidationMode.None && resolved != GtsConstants.IdPrefix + "*")
                {
                    var matches = context.Entities.Where(entity => Matches(entity.GtsId!.Id, resolved)).ToList();
                    if (matches.Count == 0)
                        errors.Add(resolved.Contains('*')
                            ? $"x-gts-ref wildcard constraint '{resolved}' has no registered match"
                            : $"x-gts-ref constraint type '{resolved}' is not registered");
                    else if (mode == GtsRefValidationMode.AnyValid && matches.All(entity => !context.ReferencedEntityValid(entity)))
                        errors.Add($"x-gts-ref constraint '{resolved}' has no valid registered match");
                }
            }
            foreach (var keyword in new[] { "properties", "patternProperties", "definitions", "$defs", "dependentSchemas" })
            {
                if (obj[keyword] is JsonObject map)
                {
                    foreach (var child in map.Select(property => property.Value))
                        WalkSchema(child, root, selectedTypeId, context, mode, errors, depth + 1);
                }
            }
            foreach (var keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
            {
                if (obj[keyword] is JsonArray branches)
                {
                    foreach (var child in branches)
                        WalkSchema(child, root, selectedTypeId, context, mode, errors, depth + 1);
                }
            }
            foreach (var keyword in new[] { "items", "additionalItems", "additionalProperties", "contains", "not", "if", "then", "else", GtsSchemaKeywords.TraitsSchema })
                WalkSchema(obj[keyword], root, selectedTypeId, context, mode, errors, depth + 1);
        }
    }

    private static void WalkInstance(
        JsonNode? instance,
        JsonNode? schema,
        JsonObject root,
        string selectedTypeId,
        RefContext context,
        GtsRefValidationMode mode,
        string path,
        List<string> errors,
        HashSet<string> localRefs,
        int depth)
    {
        if (depth >= GtsConstants.MaxNestingDepth)
            return;
        if (schema is not JsonObject obj)
            return;

        if (obj["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith('#') && localRefs.Add(reference))
        {
            if (!GtsJsonPointer.TryEvaluate(root, reference, out var target))
                errors.Add($"Local reference '{reference}' not found");
            else
                WalkInstance(instance, target, root, selectedTypeId, context, mode, path, errors, localRefs, depth + 1);
        }

        if (obj.TryGetPropertyValue(GtsSchemaKeywords.Ref, out var refNode))
        {
            if (refNode is not JsonValue patternValue || !patternValue.TryGetValue<string>(out var pattern) ||
                !TryResolvePattern(pattern, selectedTypeId, out var resolved))
                errors.Add($"{path}: invalid x-gts-ref constraint");
            else if (instance is not JsonValue instanceValue || !instanceValue.TryGetValue<string>(out var referencedId))
                errors.Add($"{path}: x-gts-ref value must be a string");
            else if (!GtsId.TryParse(referencedId, out var parsed) || parsed is null || parsed.IsInstance && parsed.Segments.Count == 1)
                errors.Add($"{path}: value is not a valid GTS identifier");
            else if (!Matches(referencedId, resolved))
                errors.Add($"{path}: value '{referencedId}' does not match pattern '{resolved}'");
            else if (mode != GtsRefValidationMode.None)
            {
                var referenced = context.FindById(referencedId);
                if (referenced is null)
                    errors.Add($"{path}: referenced entity '{referencedId}' not found in registry");
                else if (mode == GtsRefValidationMode.AnyValid && !context.ReferencedEntityValid(referenced))
                    errors.Add($"{path}: referenced entity '{referencedId}' is not valid against its GTS Type Schema");
            }
        }

        foreach (var keyword in new[] { "oneOf", "anyOf" })
        {
            if (obj[keyword] is not JsonArray branches)
                continue;
            var branchResults = new List<List<string>>();
            foreach (var branch in branches)
            {
                var branchErrors = new List<string>();
                WalkInstance(instance, branch, root, selectedTypeId, context, mode, path, branchErrors, new HashSet<string>(localRefs), depth + 1);
                branchResults.Add(branchErrors);
            }
            var matching = branchResults.Count(result => result.Count == 0);
            if (branchResults.Count > 0 && (matching == 0 || keyword == "oneOf" && matching != 1))
                errors.Add($"{path}: {keyword}: expected {(keyword == "oneOf" ? "exactly one" : "at least one")} matching x-gts-ref branch");
        }
        if (obj["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf)
                WalkInstance(instance, branch, root, selectedTypeId, context, mode, path, errors, new HashSet<string>(localRefs), depth + 1);
        }

        if (instance is JsonObject instanceObject && obj["properties"] is JsonObject properties)
        {
            foreach (var (name, propertySchema) in properties)
            {
                if (instanceObject.TryGetPropertyValue(name, out var propertyValue))
                    WalkInstance(propertyValue, propertySchema, root, selectedTypeId, context, mode,
                        string.IsNullOrEmpty(path) ? name : path + "." + name, errors, new HashSet<string>(localRefs), depth + 1);
            }
        }
        if (instance is JsonArray instanceArray)
        {
            if (obj["prefixItems"] is JsonArray prefixItems)
            {
                for (var index = 0; index < Math.Min(instanceArray.Count, prefixItems.Count); index++)
                    WalkInstance(instanceArray[index], prefixItems[index], root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
                if (obj["items"] is JsonObject overflowItems)
                {
                    for (var index = prefixItems.Count; index < instanceArray.Count; index++)
                        WalkInstance(instanceArray[index], overflowItems, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
                }
            }
            else if (obj["items"] is JsonArray tupleItems)
            {
                for (var index = 0; index < Math.Min(instanceArray.Count, tupleItems.Count); index++)
                    WalkInstance(instanceArray[index], tupleItems[index], root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
                if (obj["additionalItems"] is JsonObject additional)
                {
                    for (var index = tupleItems.Count; index < instanceArray.Count; index++)
                        WalkInstance(instanceArray[index], additional, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
                }
            }
            else if (obj["items"] is JsonObject items)
            {
                for (var index = 0; index < instanceArray.Count; index++)
                    WalkInstance(instanceArray[index], items, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
            }
        }
    }

    private static bool TryResolvePattern(string pattern, string selectedTypeId, out string resolved)
    {
        var valid = GtsRefConstraint.TryCreate(pattern, selectedTypeId, out var constraint);
        resolved = valid ? constraint.Pattern : pattern;
        return valid;
    }

    private static bool Matches(string value, string pattern) => new GtsRefConstraint(pattern).Matches(value);

    /// <summary>
    /// Per-validation lookup context: an id -&gt; entity index (built once), a lazily-built map of
    /// evaluation-normalized schemas, and a memo of referenced-entity validity. This replaces the previous
    /// per-reference linear scans and per-reference schema-map rebuilds (which were O(N) each, O(N^2) overall).
    /// </summary>
    private sealed class RefContext
    {
        private readonly Dictionary<string, GtsJsonEntity> _byId;
        private Dictionary<GtsId, JsonObject>? _normalizedSchemas;
        private readonly Dictionary<string, bool> _validity = new(StringComparer.Ordinal);

        private RefContext(List<GtsJsonEntity> entities, Dictionary<string, GtsJsonEntity> byId)
        {
            Entities = entities;
            _byId = byId;
        }

        /// <summary>Entities that carry a GTS id (the only ones referenceable by <c>x-gts-ref</c>).</summary>
        public List<GtsJsonEntity> Entities { get; }

        public static RefContext Create(IEnumerable<GtsJsonEntity> entities)
        {
            var list = entities.Where(entity => entity.GtsId is not null).ToList();
            var byId = new Dictionary<string, GtsJsonEntity>(list.Count, StringComparer.Ordinal);
            foreach (var entity in list)
                byId[entity.GtsId!.Id] = entity;
            return new RefContext(list, byId);
        }

        public GtsJsonEntity? FindById(string id) => _byId.GetValueOrDefault(id);

        private JsonObject? LoadSchema(GtsId id) =>
            _byId.TryGetValue(id.Id, out var entity) && entity.IsSchema ? entity.Content : null;

        private Dictionary<GtsId, JsonObject> NormalizedSchemas()
        {
            if (_normalizedSchemas is not null)
                return _normalizedSchemas;

            _normalizedSchemas = new Dictionary<GtsId, JsonObject>();
            foreach (var entity in Entities)
            {
                if (entity.IsSchema && entity.GtsId is not null)
                    _normalizedSchemas[entity.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(entity.Content);
            }
            return _normalizedSchemas;
        }

        public bool ReferencedEntityValid(GtsJsonEntity entity)
        {
            var key = entity.GtsId?.Id;
            if (key is not null && _validity.TryGetValue(key, out var cached))
                return cached;

            var result = ComputeValidity(entity);
            if (key is not null)
                _validity[key] = result;
            return result;
        }

        private bool ComputeValidity(GtsJsonEntity entity)
        {
            if (entity.IsSchema && entity.GtsId is not null)
                return GtsSchemaTraitsValidator.Validate(entity.GtsId, entity.Content, LoadSchema, Entities, GtsRefValidationMode.None).Count == 0;

            if (string.IsNullOrEmpty(entity.SchemaId) || !GtsId.TryParse(entity.SchemaId, out var schemaId) || schemaId is null)
                return false;

            var schemas = NormalizedSchemas();
            if (!schemas.ContainsKey(schemaId))
                return false;
            try
            {
                return GtsJsonSchemaEvaluator.Evaluate(entity.Content, schemaId, schemas).IsValid;
            }
            catch
            {
                return false;
            }
        }
    }
}
