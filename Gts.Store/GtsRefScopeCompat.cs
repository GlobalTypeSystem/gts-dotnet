using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>
/// Derivation/evolution comparison of the GTS reference keywords (README §9.6.1 "Derivation and
/// evolution", ADR-0007/0008). A property constraining a GTS reference accepts a set of strings; a
/// derived property is admissible when the strings it accepts are a subset of the base's. The two
/// entity-reference keywords compare kind-filtered §10 scopes; <c>x-gts-pattern-ref</c> compares
/// selection-set coverage. Composition (<c>allOf</c>, <c>anyOf</c>, <c>not</c>) combines scopes by
/// intersection, union and complement.
///
/// The comparison is a sound three-valued decision: <see cref="Relation.Included"/> or
/// <see cref="Relation.Excluded"/> only when provable, otherwise <see cref="Relation.Unknown"/>.
/// OP#12 admission fails closed (unknown rejected); OP#8 reports unknown. Ported from the Go/Python
/// reference (<c>ref_scope_compat</c>).
/// </summary>
public static class GtsRefScopeCompat
{
    public enum Relation
    {
        Included,
        Excluded,
        Unknown,
    }

    private const int MaxDepth = 64;
    private const int MinInstanceSegmentLen = 10; // "a.a.a.a.v0"
    private const string SelfIdRef = "/$id";

    private static readonly string[] LiteralKeywords = ["const", "enum", "default", "examples"];

    /// <summary>
    /// Three-valued reference-dimension relation <c>Valid(derived) ⊆ Valid(base)</c> over two
    /// resolved schemas, resolving each schema's <c>/$id</c> to its own type.
    /// </summary>
    public static Relation SchemaRefRelation(JsonNode? baseSchema, JsonNode? derived, string? baseSelfId, string? derivedSelfId)
    {
        // Only the base's reference keywords restrict: a derived-only constraint can only narrow,
        // and a keyword-free base imposes no restriction (README §9.6.1). The scan is far cheaper
        // than the deep copy and scope walk, so bail out early for the common keyword-free base.
        if (!MentionsRefKeyword(baseSchema, wantSelfId: false))
            return Relation.Included;
        var baseEval = WithResolvedSelf(baseSchema, baseSelfId ?? SelfIdOf(baseSchema));
        var derivedEval = WithResolvedSelf(derived, derivedSelfId ?? SelfIdOf(derived));
        return SchemaRelation(baseEval, derivedEval, 0);
    }

    private enum Kind { Top, Atom, Union, Inter, Not, Undecidable }

    private sealed class Scope(Kind kind)
    {
        public Kind Kind { get; } = kind;
        public string? RefKeyword { get; set; }
        public GtsPattern? Pattern { get; set; }
        public bool Empty { get; set; }
        public List<Scope> Children { get; set; } = [];
        public Scope? Child { get; set; }
    }

    private static bool IsPatternRef(string keyword) => keyword == GtsSchemaKeywords.PatternRef;

    private static Scope? RefAtom(string keyword, JsonNode? operand)
    {
        if (operand is not JsonValue value || !value.TryGetValue<string>(out var spelling) || spelling == SelfIdRef)
            return null;
        if (!GtsPattern.TryParse(spelling, out var pattern))
            return null;
        var atom = new Scope(Kind.Atom) { RefKeyword = keyword, Pattern = pattern };
        // An instance reference to a concrete Type ID too long for any instance tail selects nothing.
        if (keyword == GtsSchemaKeywords.InstanceRef && !pattern!.HasWildcard && spelling.EndsWith('~')
            && spelling.Length + MinInstanceSegmentLen > GtsPattern.MaxLength)
            atom.Empty = true;
        return atom;
    }

    private static (Scope Scope, bool Has) BuildRefScope(JsonNode? prop, int depth)
    {
        if (prop is not JsonObject obj)
            return (new Scope(Kind.Top), false);
        if (depth >= MaxDepth)
            return (new Scope(Kind.Undecidable), true);

        var parts = new List<Scope>();
        var has = false;

        foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
        {
            if (!obj.TryGetPropertyValue(keyword, out var operand))
                continue;
            has = true;
            var atom = RefAtom(keyword, operand);
            if (atom is not null)
                parts.Add(atom);
        }

        if (obj["not"] is JsonObject notValue)
        {
            var (childScope, childHas) = BuildRefScope(notValue, depth + 1);
            if (childHas)
            {
                has = true;
                parts.Add(new Scope(Kind.Not) { Child = childScope });
            }
        }

        foreach (var key in new[] { "anyOf", "oneOf" })
        {
            if (obj[key] is not JsonArray branches)
                continue;
            var union = new List<Scope>();
            var branchHas = false;
            foreach (var branch in branches)
            {
                if (branch is not JsonObject)
                {
                    union.Add(new Scope(Kind.Top));
                    continue;
                }
                var (bs, bh) = BuildRefScope(branch, depth + 1);
                if (bh)
                    branchHas = true;
                union.Add(bs);
            }
            if (branchHas)
            {
                has = true;
                parts.Add(new Scope(Kind.Union) { Children = union });
            }
        }

        if (obj["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf)
            {
                if (branch is not JsonObject)
                    continue;
                var (bs, bh) = BuildRefScope(branch, depth + 1);
                if (bh)
                {
                    has = true;
                    parts.Add(bs);
                }
            }
        }

        if (parts.Count == 0)
            return (new Scope(Kind.Top), has);
        if (parts.Count == 1)
            return (parts[0], has);
        return (new Scope(Kind.Inter) { Children = parts }, has);
    }

    private static Relation RelationOf(Scope baseScope, Scope derived)
    {
        if (baseScope.Kind == Kind.Undecidable || derived.Kind == Kind.Undecidable)
            return Relation.Unknown;
        if (baseScope.Kind == Kind.Top)
            return Relation.Included;
        if (derived.Kind == Kind.Top)
            return Relation.Excluded;

        switch (derived.Kind)
        {
            case Kind.Union:
                var unionResult = Relation.Included;
                foreach (var child in derived.Children)
                {
                    var r = RelationOf(baseScope, child);
                    if (r == Relation.Excluded)
                        return Relation.Excluded;
                    if (r == Relation.Unknown)
                        unionResult = Relation.Unknown;
                }
                return unionResult;
            case Kind.Inter:
                foreach (var child in derived.Children)
                    if (RelationOf(baseScope, child) == Relation.Included)
                        return Relation.Included;
                return Relation.Unknown;
            case Kind.Not:
                // complement(D) ⊆ complement(B) ⟺ B ⊆ D.
                if (baseScope.Kind == Kind.Not)
                    return RelationOf(derived.Child!, baseScope.Child!);
                return Relation.Unknown;
        }

        // Derived is an atom; dispatch on the base side.
        switch (baseScope.Kind)
        {
            case Kind.Union:
                foreach (var child in baseScope.Children)
                    if (RelationOf(child, derived) == Relation.Included)
                        return Relation.Included;
                return Relation.Unknown;
            case Kind.Inter:
                var interResult = Relation.Included;
                foreach (var child in baseScope.Children)
                {
                    var r = RelationOf(child, derived);
                    if (r == Relation.Excluded)
                        return Relation.Excluded;
                    if (r == Relation.Unknown)
                        interResult = Relation.Unknown;
                }
                return interResult;
            case Kind.Atom:
                return AtomRelation(baseScope, derived);
            default:
                return Relation.Unknown;
        }
    }

    private static Relation AtomRelation(Scope baseScope, Scope derived)
    {
        if (derived.Empty)
            return Relation.Included;
        if (IsPatternRef(baseScope.RefKeyword!) != IsPatternRef(derived.RefKeyword!))
            return Relation.Unknown;
        if (baseScope.RefKeyword != derived.RefKeyword)
            return Relation.Excluded; // type vs instance: disjoint nonempty scopes
        return baseScope.Pattern!.Covers(derived.Pattern!) ? Relation.Included : Relation.Excluded;
    }

    private static Relation RefScopeRelation(JsonNode? baseProp, JsonNode? derivedProp)
    {
        var (baseScope, baseHas) = BuildRefScope(baseProp, 0);
        if (!baseHas)
            return Relation.Included; // base imposes no reference restriction
        var (derivedScope, derivedHas) = BuildRefScope(derivedProp, 0);
        if (derivedHas)
            return RelationOf(baseScope, derivedScope);
        var (values, enumerates) = CollectEnumeratedValues(derivedProp);
        if (!enumerates)
            return Relation.Excluded; // a plain string/regex accepts values outside the base scope
        foreach (var value in values)
        {
            if (value is not JsonValue jv || !jv.TryGetValue<string>(out var text) || !ScopeAccepts(baseScope, text))
                return Relation.Excluded;
        }
        return Relation.Included;
    }

    private static (List<JsonNode?> Values, bool Enumerates) CollectEnumeratedValues(JsonNode? prop)
    {
        if (prop is not JsonObject obj)
            return ([], false);
        if (obj.TryGetPropertyValue("const", out var constValue))
            return ([constValue], true);
        if (obj["enum"] is JsonArray enumArray)
            return ([.. enumArray], true);
        return ([], false);
    }

    private static bool ScopeAccepts(Scope scope, string value)
    {
        switch (scope.Kind)
        {
            case Kind.Top:
                return true;
            case Kind.Not:
                return !ScopeAccepts(scope.Child!, value);
            case Kind.Union:
                return scope.Children.Any(c => ScopeAccepts(c, value));
            case Kind.Inter:
                return scope.Children.All(c => ScopeAccepts(c, value));
            case Kind.Atom:
                if (scope.Empty)
                    return false;
                return AtomAccepts(scope.RefKeyword!, scope.Pattern!, value);
            default:
                return false;
        }
    }

    /// <summary>Whether a single reference atom accepts the candidate string (kind + match / coverage).</summary>
    private static bool AtomAccepts(string keyword, GtsPattern operandPattern, string value)
    {
        if (IsPatternRef(keyword))
            return GtsPattern.TryParse(value, out var candidatePattern) && operandPattern.Covers(candidatePattern!);
        if (!GtsPattern.TryParse(value, out var concrete) || concrete!.HasWildcard)
            return false;
        var requireType = keyword == GtsSchemaKeywords.TypeRef;
        if (value.EndsWith('~') != requireType)
            return false;
        return operandPattern.MatchesCandidate(value);
    }

    // --- effective-property folding (allOf) ------------------------------------------------

    private static (JsonNode? Operand, bool Ok) NarrowerRefOperand(string keyword, JsonNode? a, JsonNode? b)
    {
        var atomA = RefAtom(keyword, a);
        var atomB = RefAtom(keyword, b);
        if (atomA is null || atomB is null)
            return (null, false);
        if (atomA.Pattern!.Covers(atomB.Pattern!))
            return (b, true); // b ⊆ a
        if (atomB.Pattern!.Covers(atomA.Pattern!))
            return (a, true); // a ⊆ b
        return (null, false);
    }

    private static JsonNode? MergeEffectiveProperty(JsonNode? prev, JsonNode? next)
    {
        if (prev is not JsonObject prevObj || next is not JsonObject nextObj)
            return next;
        var merged = (JsonObject)nextObj.DeepClone();
        foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
        {
            if (!prevObj.TryGetPropertyValue(keyword, out var prevOperand))
                continue;
            if (!merged.ContainsKey(keyword))
            {
                merged[keyword] = prevOperand?.DeepClone();
                continue;
            }
            var (narrower, ok) = NarrowerRefOperand(keyword, prevOperand, merged[keyword]);
            if (ok)
            {
                merged[keyword] = narrower?.DeepClone();
                continue;
            }
            // Incomparable operands: keep both as a conjunction.
            var nextOperand = merged[keyword];
            merged.Remove(keyword);
            var both = merged["allOf"] is JsonArray existing ? (JsonArray)existing.DeepClone() : new JsonArray();
            both.Add(new JsonObject { [keyword] = prevOperand?.DeepClone() });
            both.Add(new JsonObject { [keyword] = nextOperand?.DeepClone() });
            merged["allOf"] = both;
        }
        return merged;
    }

    /// <summary>
    /// The effective child subschemas a schema contributes, keyed by a stable position so the base
    /// and derived sides line up: object properties, array <c>items</c> (single or tuple) and
    /// <c>prefixItems</c>, and <c>additionalProperties</c>. Reference keywords apply at every such
    /// location (§9.6.3), so the derivation/evolution comparison must recurse into all of them, not
    /// only <c>properties</c>. <c>allOf</c> branches are folded in with reference-aware merging.
    /// </summary>
    private static Dictionary<string, JsonNode?> EffectiveChildren(JsonNode? schema, int depth)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (schema is not JsonObject obj || depth >= MaxDepth)
            return result;

        void Add(string key, JsonNode? value) => result[key] = MergeEffectiveProperty(result.GetValueOrDefault(key), value);

        if (obj["allOf"] is JsonArray allOf)
            foreach (var branch in allOf)
                foreach (var (key, value) in EffectiveChildren(branch, depth + 1))
                    Add(key, value);
        if (obj["properties"] is JsonObject properties)
            foreach (var (name, prop) in properties)
                Add("p:" + name, prop);
        switch (obj["items"])
        {
            case JsonObject itemsSchema:
                Add("items", itemsSchema);
                break;
            case JsonArray tupleItems:
                for (var i = 0; i < tupleItems.Count; i++)
                    Add($"i:{i}", tupleItems[i]);
                break;
        }
        if (obj["prefixItems"] is JsonArray prefixItems)
            for (var i = 0; i < prefixItems.Count; i++)
                Add($"pi:{i}", prefixItems[i]);
        if (obj["additionalProperties"] is JsonObject additionalProperties)
            Add("additionalProperties", additionalProperties);
        if (obj["additionalItems"] is JsonObject additionalItems)
            Add("additionalItems", additionalItems);
        return result;
    }

    // --- self-reference resolution ---------------------------------------------------------

    private static void ResolveSelfRefOperands(JsonNode? node, string selfId, int depth)
    {
        if (depth >= MaxDepth)
            return;
        if (node is JsonObject obj)
        {
            foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
                if (obj[keyword] is JsonValue v && v.TryGetValue<string>(out var s) && s == SelfIdRef)
                    obj[keyword] = selfId;
            foreach (var value in obj.Select(property => property.Value))
                ResolveSelfRefOperands(value, selfId, depth + 1);
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array)
                ResolveSelfRefOperands(value, selfId, depth + 1);
        }
    }

    private static string? SelfIdOf(JsonNode? schema) =>
        schema is JsonObject obj && obj["$id"] is JsonValue v && v.TryGetValue<string>(out var id)
            ? GtsConstants.StripUriPrefix(id)
            : null;

    private static Relation Combine(Relation current, Relation child)
    {
        if (child == Relation.Excluded)
            return Relation.Excluded;
        if (child == Relation.Unknown && current != Relation.Excluded)
            return Relation.Unknown;
        return current;
    }

    private static Relation SchemaRelation(JsonNode? baseSchema, JsonNode? derived, int depth)
    {
        if (depth >= MaxDepth)
            return Relation.Unknown;
        var result = RefScopeRelation(baseSchema, derived);
        if (result == Relation.Excluded)
            return Relation.Excluded;
        var baseProps = EffectiveChildren(baseSchema, 0);
        var derivedProps = EffectiveChildren(derived, 0);
        foreach (var name in baseProps.Keys.Union(derivedProps.Keys))
        {
            var child = SchemaRelation(baseProps.GetValueOrDefault(name), derivedProps.GetValueOrDefault(name), depth + 1);
            result = Combine(result, child);
            if (result == Relation.Excluded)
                return Relation.Excluded;
        }
        return result;
    }

    private static bool MentionsRefKeyword(JsonNode? node, bool wantSelfId)
    {
        if (node is JsonObject obj)
        {
            foreach (var keyword in GtsSchemaKeywords.ReferenceKeywords)
                if (obj.TryGetPropertyValue(keyword, out var operand) &&
                    (!wantSelfId || (operand is JsonValue v && v.TryGetValue<string>(out var s) && s == SelfIdRef)))
                    return true;
            return obj.Any(property => !LiteralKeywords.Contains(property.Key) && MentionsRefKeyword(property.Value, wantSelfId));
        }
        return node is JsonArray array && array.Any(item => MentionsRefKeyword(item, wantSelfId));
    }

    private static JsonNode? WithResolvedSelf(JsonNode? schema, string? selfId)
    {
        if (selfId is null || !MentionsRefKeyword(schema, wantSelfId: true))
            return schema;
        var copy = schema!.DeepClone();
        ResolveSelfRefOperands(copy, selfId, 0);
        return copy;
    }
}
