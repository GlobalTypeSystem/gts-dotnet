using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

/// <summary>
/// Validates the three GTS identifier-reference keywords (README §9.6): <c>x-gts-type-ref</c> and
/// <c>x-gts-instance-ref</c> (entity references, §9.6.1) and <c>x-gts-pattern-ref</c> (pattern-valued
/// fields, §9.6.2). Operand validity (schema errors) is checked by walking schema positions;
/// candidate/value validity (ordinary validation failures, subject to the dialect's applicators) is
/// checked by walking the instance against the schema.
///
/// Matching and coverage come from the §10 engine (<see cref="GtsPattern"/>). Entity-reference
/// candidates additionally require the right kind (Type IDs end in <c>~</c>; Instance IDs do not) and
/// the selected registry policy; pattern references are registry-independent (§9.6.2).
/// </summary>
public static class GtsRefValidator
{
    private enum RefKind
    {
        Type,
        Instance,
        Pattern,
    }

    private static RefKind? KindOf(string keyword) => keyword switch
    {
        GtsSchemaKeywords.TypeRef => RefKind.Type,
        GtsSchemaKeywords.InstanceRef => RefKind.Instance,
        GtsSchemaKeywords.PatternRef => RefKind.Pattern,
        _ => null,
    };

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
        WalkSchema(schema, selectedTypeId, RefContext.Create(entities), mode, errors, 0);
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

    /// <summary>The selected leaf Type ID that <c>/$id</c> resolves to, stripped of <c>gts://</c>.</summary>
    private static string SelfId(string selectedTypeId) => GtsConstants.StripUriPrefix(selectedTypeId);

    /// <summary>The resolved operand of a reference keyword, or why it is a schema error.</summary>
    private readonly record struct Operand(RefKind Kind, GtsPattern Pattern, bool IsWildcard, string? ConcreteId, bool IsSelfId);

    /// <summary>
    /// Resolves a keyword operand to a §10 pattern, enforcing the operand contract (§9.6.1/§9.6.2).
    /// Returns <c>false</c> and sets <paramref name="error"/> for a schema error.
    /// </summary>
    private static bool TryResolveOperand(RefKind kind, JsonNode? value, string selectedTypeId, out Operand operand, out string error)
    {
        operand = default;
        error = "";
        var keyword = KeywordName(kind);
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var spelling))
        {
            error = $"{keyword} operand must be a string";
            return false;
        }

        if (spelling == "/$id")
        {
            var selected = SelfId(selectedTypeId);
            // Kind is read from the spelling, not the int-based GtsId parser, so unsigned 32-bit
            // versions are accepted.
            if (string.IsNullOrEmpty(selected) || !GtsPattern.TryParse(selected, out var selfPattern) ||
                selfPattern!.HasWildcard || !selected.EndsWith('~'))
            {
                error = $"{keyword} operand '/$id' resolves to '{selected}', which is not a concrete GTS Type Identifier";
                return false;
            }
            operand = new Operand(kind, selfPattern, false, selected, IsSelfId: true);
            return true;
        }

        if (!GtsPattern.TryParse(spelling, out var pattern))
        {
            error = $"{keyword} operand '{spelling}' is not a GTS identifier, a wildcard pattern or '/$id'";
            return false;
        }

        // A concrete Instance ID selects no Type IDs: it is a schema error under x-gts-type-ref.
        // Kind is determined from the parsed form (ends in `~`), not the int-based GtsId parser,
        // so that unsigned 32-bit versions are handled.
        if (kind == RefKind.Type && !pattern!.HasWildcard && !spelling.EndsWith('~'))
        {
            error = $"{keyword} operand '{spelling}' is a GTS Instance Identifier, which selects no GTS Type Identifiers";
            return false;
        }

        operand = new Operand(kind, pattern!, pattern!.HasWildcard, pattern.HasWildcard ? null : spelling, IsSelfId: false);
        return true;
    }

    private static string KeywordName(RefKind kind) => kind switch
    {
        RefKind.Type => GtsSchemaKeywords.TypeRef,
        RefKind.Instance => GtsSchemaKeywords.InstanceRef,
        _ => GtsSchemaKeywords.PatternRef,
    };

    // ---- schema walk: operand validity + registry operand checks --------------------------

    private static void WalkSchema(
        JsonNode? node,
        string selectedTypeId,
        RefContext context,
        GtsRefValidationMode mode,
        List<string> errors,
        int depth)
    {
        if (depth >= GtsConstants.MaxNestingDepth || node is not JsonObject obj)
            return;

        foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
        {
            if (!obj.TryGetPropertyValue(keyword, out var operandNode))
                continue;
            var kind = KindOf(keyword)!.Value;
            if (!TryResolveOperand(kind, operandNode, selectedTypeId, out var operand, out var error))
            {
                errors.Add(error);
                continue;
            }
            // Pattern references never consult the registry (§9.6.2); the `/$id` self-reference
            // resolves to the type being validated and is not a registry lookup.
            if (kind != RefKind.Pattern && mode != GtsRefValidationMode.None && !operand.IsSelfId)
                CheckOperandRegistry(operand, context, mode, errors);
        }

        foreach (var keyword in new[] { "properties", "patternProperties", "definitions", "$defs", "dependentSchemas" })
        {
            if (obj[keyword] is JsonObject map)
                foreach (var child in map.Select(property => property.Value))
                    WalkSchema(child, selectedTypeId, context, mode, errors, depth + 1);
        }
        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
        {
            if (obj[keyword] is JsonArray branches)
                foreach (var child in branches)
                    WalkSchema(child, selectedTypeId, context, mode, errors, depth + 1);
        }
        foreach (var keyword in new[] { "items", "additionalItems", "additionalProperties", "contains", "not", "if", "then", "else", GtsSchemaKeywords.TraitsSchema })
            WalkSchema(obj[keyword], selectedTypeId, context, mode, errors, depth + 1);
    }

    private static void CheckOperandRegistry(Operand operand, RefContext context, GtsRefValidationMode mode, List<string> errors)
    {
        var keyword = KeywordName(operand.Kind);
        if (operand.IsWildcard)
        {
            var matches = context.Entities.Where(entity => operand.Pattern.Matches(entity.GtsId!)).ToList();
            if (matches.Count == 0)
                errors.Add($"{keyword} wildcard operand '{operand.Pattern.Pattern}' has no registered match");
            else if (mode == GtsRefValidationMode.AnyValid && matches.All(entity => !context.ReferencedEntityValid(entity)))
                errors.Add($"{keyword} wildcard operand '{operand.Pattern.Pattern}' has no valid registered match");
            return;
        }

        var target = context.FindById(operand.ConcreteId!);
        if (target is null)
            errors.Add($"{keyword} operand '{operand.ConcreteId}' is not registered");
        else if (mode == GtsRefValidationMode.AnyValid && !context.ReferencedEntityValid(target))
            errors.Add($"{keyword} operand '{operand.ConcreteId}' is not valid");
    }

    // ---- instance walk: candidate / value validity ----------------------------------------

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
        if (depth >= GtsConstants.MaxNestingDepth || schema is not JsonObject obj)
            return;

        if (obj["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith('#') && localRefs.Add(reference + "\0" + path))
        {
            if (!GtsJsonPointer.TryEvaluate(root, reference, out var target))
                errors.Add($"Local reference '{reference}' not found");
            else
                WalkInstance(instance, target, root, selectedTypeId, context, mode, path, errors, localRefs, depth + 1);
        }

        foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
        {
            if (!obj.TryGetPropertyValue(keyword, out var operandNode))
                continue;
            var kind = KindOf(keyword)!.Value;
            // An invalid operand is a schema error reported by the schema walk; skip value checks.
            if (!TryResolveOperand(kind, operandNode, selectedTypeId, out var operand, out _))
                continue;
            CheckCandidate(instance, operand, context, mode, path, errors);
        }

        foreach (var keyword in new[] { "oneOf", "anyOf" })
        {
            if (obj[keyword] is not JsonArray branches)
                continue;
            // Only branches that actually declare a reference keyword participate in the match count;
            // a branch without one (e.g. the {"type":"null"} arm of a nullable reference) is neutral
            // and evaluated by the JSON Schema engine. A neutral branch only suppresses a reference
            // failure when it could actually match this value: a {"type":"null"} branch cannot rescue
            // a string value, so its sibling reference branches must still be satisfied.
            var refBranchResults = new List<List<string>>();
            var hasNeutralBranch = false;
            foreach (var branch in branches)
            {
                if (!ContainsReferenceKeyword(branch))
                {
                    if (BranchTypeAdmits(branch, instance))
                        hasNeutralBranch = true;
                    continue;
                }
                var branchErrors = new List<string>();
                WalkInstance(instance, branch, root, selectedTypeId, context, mode, path, branchErrors, new HashSet<string>(localRefs), depth + 1);
                refBranchResults.Add(branchErrors);
            }
            var matching = refBranchResults.Count(result => result.Count == 0);
            if (refBranchResults.Count > 0 && (matching == 0 && !hasNeutralBranch || keyword == "oneOf" && matching > 1))
                errors.Add($"{path}: {keyword}: expected {(keyword == "oneOf" ? "exactly one" : "at least one")} matching reference branch");
        }
        if (obj["allOf"] is JsonArray allOf)
            foreach (var branch in allOf)
                WalkInstance(instance, branch, root, selectedTypeId, context, mode, path, errors, new HashSet<string>(localRefs), depth + 1);

        // `not` inverts a reference value-verdict (§9.6.3): if the negated subschema's reference
        // constraints are satisfied, `not` fails. Neutral subschemas (no reference keyword) are left
        // to the JSON Schema engine. Nested `not` composes through recursion.
        if (obj["not"] is JsonObject notSchema && ContainsReferenceKeyword(notSchema))
        {
            var notErrors = new List<string>();
            WalkInstance(instance, notSchema, root, selectedTypeId, context, mode, path, notErrors, new HashSet<string>(localRefs), depth + 1);
            if (notErrors.Count == 0 && StructurallyAccepts(instance, notSchema, root))
                errors.Add($"{path}: not: reference constraint unexpectedly satisfied");
        }

        // `if` selects the branch that applies; its own failures are not errors. Only the active
        // branch is checked. A reference-bearing `if` hides the whole conditional from the JSON
        // Schema engine (GtsSchemaDocumentNormalizer), so the active branch's structural part is
        // checked here too.
        if (obj["if"] is { } ifSchema && (ContainsReferenceKeyword(ifSchema) || ContainsReferenceKeyword(obj["then"]) || ContainsReferenceKeyword(obj["else"])))
        {
            var ifErrors = new List<string>();
            WalkInstance(instance, ifSchema, root, selectedTypeId, context, mode, path, ifErrors, new HashSet<string>(localRefs), depth + 1);
            var conditionHolds = ifSchema switch
            {
                JsonObject ifObject => ifErrors.Count == 0 && StructurallyAccepts(instance, ifObject, root),
                JsonValue value => value.TryGetValue<bool>(out var holds) && holds,
                _ => false,
            };
            var branchKeyword = conditionHolds ? "then" : "else";
            var branch = obj[branchKeyword];
            WalkInstance(instance, branch, root, selectedTypeId, context, mode, path, errors, new HashSet<string>(localRefs), depth + 1);
            if (ContainsReferenceKeyword(ifSchema) && !BranchStructurallyAccepts(instance, branch, root))
                errors.Add($"{path}: {branchKeyword}: value does not match the branch selected by a reference condition");
        }

        if (instance is JsonObject instanceObject)
            WalkObject(instanceObject, obj, root, selectedTypeId, context, mode, path, errors, localRefs, depth);

        if (instance is JsonArray instanceArray)
            WalkArray(instanceArray, obj, root, selectedTypeId, context, mode, path, errors, localRefs, depth);
    }

    private static void WalkObject(
        JsonObject instanceObject, JsonObject obj, JsonObject root, string selectedTypeId,
        RefContext context, GtsRefValidationMode mode, string path, List<string> errors, HashSet<string> localRefs, int depth)
    {
        var properties = obj["properties"] as JsonObject;
        var patternProperties = obj["patternProperties"] as JsonObject;
        foreach (var (name, value) in instanceObject)
        {
            var childPath = string.IsNullOrEmpty(path) ? name : path + "." + name;
            var covered = false;
            if (properties is not null && properties.TryGetPropertyValue(name, out var propertySchema))
            {
                covered = true;
                WalkInstance(value, propertySchema, root, selectedTypeId, context, mode, childPath, errors, new HashSet<string>(localRefs), depth + 1);
            }
            if (patternProperties is not null)
            {
                foreach (var (key, patternSchema) in patternProperties)
                    if (NameMatchesPattern(key, name))
                    {
                        covered = true;
                        WalkInstance(value, patternSchema, root, selectedTypeId, context, mode, childPath, errors, new HashSet<string>(localRefs), depth + 1);
                    }
            }
            if (!covered && obj["additionalProperties"] is JsonObject additionalProperties)
                WalkInstance(value, additionalProperties, root, selectedTypeId, context, mode, childPath, errors, new HashSet<string>(localRefs), depth + 1);
            if (!covered && obj["unevaluatedProperties"] is JsonObject unevaluatedProperties)
                WalkInstance(value, unevaluatedProperties, root, selectedTypeId, context, mode, childPath, errors, new HashSet<string>(localRefs), depth + 1);
        }
    }

    private static bool NameMatchesPattern(string pattern, string name)
    {
        try
        {
            return GtsRegexCompiler.Compile(pattern).IsMatch(name);
        }
        catch (GtsRegexException)
        {
            return false;
        }
    }

    private static void WalkArray(
        JsonArray instanceArray, JsonObject obj, JsonObject root, string selectedTypeId,
        RefContext context, GtsRefValidationMode mode, string path, List<string> errors, HashSet<string> localRefs, int depth)
    {
        if (obj["prefixItems"] is JsonArray prefixItems)
        {
            for (var index = 0; index < Math.Min(instanceArray.Count, prefixItems.Count); index++)
                WalkInstance(instanceArray[index], prefixItems[index], root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
            if (obj["items"] is JsonObject overflowItems)
                for (var index = prefixItems.Count; index < instanceArray.Count; index++)
                    WalkInstance(instanceArray[index], overflowItems, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
        }
        else if (obj["items"] is JsonArray tupleItems)
        {
            for (var index = 0; index < Math.Min(instanceArray.Count, tupleItems.Count); index++)
                WalkInstance(instanceArray[index], tupleItems[index], root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
            if (obj["additionalItems"] is JsonObject additional)
                for (var index = tupleItems.Count; index < instanceArray.Count; index++)
                    WalkInstance(instanceArray[index], additional, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
        }
        else if (obj["items"] is JsonObject items)
        {
            for (var index = 0; index < instanceArray.Count; index++)
                WalkInstance(instanceArray[index], items, root, selectedTypeId, context, mode, $"{path}[{index}]", errors, new HashSet<string>(localRefs), depth + 1);
        }
    }

    /// <summary>Checks a candidate value against a resolved reference operand (ordinary value failure on mismatch).</summary>
    private static void CheckCandidate(JsonNode? instance, Operand operand, RefContext context, GtsRefValidationMode mode, string path, List<string> errors)
    {
        // Reference keywords constrain strings only; other JSON types are not constrained here.
        if (instance is not JsonValue value || !value.TryGetValue<string>(out var candidate))
            return;
        var keyword = KeywordName(operand.Kind);

        if (operand.Kind == RefKind.Pattern)
        {
            if (!GtsPattern.TryParse(candidate, out var candidatePattern))
                errors.Add($"{path}: '{candidate}' is not a valid GTS identifier pattern");
            else if (!operand.Pattern.Covers(candidatePattern!))
                errors.Add($"{path}: '{candidate}' selects GTS identifiers outside '{operand.Pattern.Pattern}'");
            return;
        }

        // Candidate must be a concrete canonical identifier — not a pattern or a gts:// URI.
        // Kind is read from the spelling (Type IDs end in `~`), not the int-based GtsId parser,
        // so unsigned 32-bit versions are accepted.
        if (!GtsPattern.TryParse(candidate, out var concrete) || concrete!.HasWildcard)
        {
            errors.Add($"{path}: value '{candidate}' is not a valid GTS identifier");
            return;
        }
        var candidateIsType = candidate.EndsWith('~');
        var requireType = operand.Kind == RefKind.Type;
        if (candidateIsType != requireType)
        {
            errors.Add($"{path}: value '{candidate}' is a GTS {(candidateIsType ? "Type" : "Instance")} Identifier, but {keyword} requires a GTS {(requireType ? "Type" : "Instance")} Identifier");
            return;
        }
        if (!operand.Pattern.MatchesCandidate(candidate))
        {
            errors.Add($"{path}: value '{candidate}' does not match pattern '{operand.Pattern.Pattern}'");
            return;
        }
        if (mode != GtsRefValidationMode.None)
        {
            var referenced = context.FindById(candidate);
            if (referenced is null)
                errors.Add($"{path}: referenced entity '{candidate}' is not registered");
            else if (mode == GtsRefValidationMode.AnyValid && !context.ReferencedEntityValid(referenced))
                errors.Add($"{path}: referenced entity '{candidate}' is not valid against its GTS Type Schema");
        }
    }

    /// <summary>
    /// Whether <paramref name="branch"/>'s <c>type</c> constraint (if any) admits the JSON type of
    /// <paramref name="instance"/>. Used to decide whether a reference-free combinator branch could
    /// match the value; a branch with no <c>type</c> is assumed to possibly match.
    /// </summary>
    private static bool BranchTypeAdmits(JsonNode? branch, JsonNode? instance)
    {
        if (branch is not JsonObject obj || !obj.TryGetPropertyValue("type", out var typeNode))
            return true;
        var actual = JsonTypeName(instance);
        bool Admits(JsonNode? t) => t is JsonValue v && v.TryGetValue<string>(out var name) &&
            (name == actual || (name == "number" && actual == "integer"));
        return typeNode switch
        {
            JsonArray types => types.Any(Admits),
            _ => Admits(typeNode),
        };
    }

    private static string JsonTypeName(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return "null";
            case JsonObject:
                return "object";
            case JsonArray:
                return "array";
            case JsonValue value:
                return value.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.String => "string",
                    System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
                    System.Text.Json.JsonValueKind.Null => "null",
                    // JSON Schema treats any number with a zero fractional part (e.g. 1.0) as an integer.
                    System.Text.Json.JsonValueKind.Number => value.GetValue<double>() is var number && double.IsFinite(number) && Math.Floor(number) == number ? "integer" : "number",
                    _ => "",
                };
            default:
                return "";
        }
    }

    /// <summary>
    /// Whether the JSON Schema engine accepts <paramref name="instance"/> against the non-reference
    /// part of <paramref name="subschema"/>. The engine never sees a reference-bearing <c>not</c>,
    /// so its structural constraints (e.g. <c>type</c>) are checked here before inverting. Root
    /// definitions and dialect are carried over so local references resolve. Fails closed (assumes
    /// acceptance, so <c>not</c> rejects) when the subschema cannot be evaluated.
    /// </summary>
    private static bool StructurallyAccepts(JsonNode? instance, JsonObject subschema, JsonObject root)
    {
        var document = (JsonObject)subschema.DeepClone();
        foreach (var key in new[] { "$schema", "$$schema", "$defs", "$$defs", "definitions" })
            if (root[key] is { } value && !document.ContainsKey(key))
                document[key] = value.DeepClone();
        try
        {
            return GtsJsonSchemaEvaluator.EvaluateInline(instance, GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(document)).IsValid;
        }
        catch (Json.Schema.JsonSchemaException)
        {
            return true;
        }
    }

    /// <summary>
    /// <see cref="StructurallyAccepts"/> for a conditional branch, which may be absent (accepts) or
    /// a boolean schema.
    /// </summary>
    private static bool BranchStructurallyAccepts(JsonNode? instance, JsonNode? branch, JsonObject root) => branch switch
    {
        null => true,
        JsonObject branchObject => StructurallyAccepts(instance, branchObject, root),
        JsonValue value => value.TryGetValue<bool>(out var accepts) && accepts,
        _ => false,
    };

    /// <summary>Whether a schema node carries any of the three reference keywords (directly or nested).</summary>
    private static bool ContainsReferenceKeyword(JsonNode? node) => node switch
    {
        JsonObject obj => GtsSchemaKeywords.ReferenceKeywords.Any(obj.ContainsKey) || obj.Any(property => ContainsReferenceKeyword(property.Value)),
        JsonArray array => array.Any(ContainsReferenceKeyword),
        _ => false,
    };

    /// <summary>
    /// Per-validation lookup context: an id -&gt; entity index, a lazily-built map of
    /// evaluation-normalized schemas, and a memo of referenced-entity validity.
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
                if (entity.IsSchema && entity.GtsId is not null)
                    _normalizedSchemas[entity.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(entity.Content);
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
            {
                // A type target is valid only if its derivation holds (e.g. it does not derive from a
                // final ancestor) and its traits validate (§9.6.1 "any-valid").
                var (derivationOk, _) = GtsSchemaDerivationValidator.ValidateAgainstRegistry(entity.GtsId, entity.Content, LoadSchema);
                return derivationOk &&
                    GtsSchemaTraitsValidator.Validate(entity.GtsId, entity.Content, LoadSchema, Entities, GtsRefValidationMode.None).Count == 0;
            }

            if (string.IsNullOrEmpty(entity.SchemaId) || !GtsId.TryParse(entity.SchemaId, out var schemaId) || schemaId is null)
                return false;

            // An instance of an x-gts-abstract type is not a valid entity (abstract types are non-instantiable).
            if (LoadSchema(schemaId) is { } typeSchema &&
                typeSchema[GtsSchemaKeywords.Abstract] is JsonValue abstractValue &&
                abstractValue.TryGetValue<bool>(out var isAbstract) && isAbstract)
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
