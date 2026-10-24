using System.Text.Json.Nodes;

namespace Gts.Store.Validation;

/// <summary>
/// Checks every regular expression in the schema positions of a Type Schema document against the
/// GTS profile (<see cref="GtsRegexProfile"/>, README §11.0.1). Mirrors the Rust reference's
/// <c>schema_regex</c>: it visits active and inactive schema positions, the root
/// <c>x-gts-traits-schema</c>, and the initial targets of <c>$ref</c> (local JSON pointers and
/// <c>gts://</c> references), so a <c>pattern</c> or <c>patternProperties</c> key reached only
/// through a reference is still checked. Literal data (<c>const</c>, <c>enum</c>, <c>default</c>,
/// <c>examples</c>, <c>x-gts-traits</c>) and unknown keywords are visited only through references.
///
/// This complements JSON-Schema compilation, which resolves external <c>$ref</c> targets lazily and
/// would otherwise miss an unsupported expression that never needs to be evaluated.
/// </summary>
internal static class GtsSchemaRegexValidator
{
    /// <summary>
    /// Returns the first unsupported expression's diagnostic, or <c>null</c> when every expression
    /// in the reachable schema graph is in profile. <paramref name="load"/> resolves a
    /// <c>gts://</c> reference target to its document.
    /// </summary>
    internal static string? Check(JsonObject document, Func<GtsId, JsonObject?> load)
    {
        var walk = new Walk(load);
        return walk.Node(document, document, Draft(document), "#");
    }

    private enum DraftKind
    {
        Draft07,
        Draft201909,
        Draft202012,
    }

    private static DraftKind Draft(JsonObject document) =>
        GtsTypeSchema.TryGetSupportedDialect(document, out var dialect, out _)
            ? dialect switch
            {
                "2019-09" => DraftKind.Draft201909,
                "2020-12" => DraftKind.Draft202012,
                _ => DraftKind.Draft07,
            }
            : DraftKind.Draft07;

    private enum Holds
    {
        Schema,
        Array,
        Map,
        SchemaOrArray,
        Dependencies,
    }

    /// <summary>
    /// Which keywords hold subschemas, per draft. This intentionally re-encodes knowledge that
    /// JsonSchema.Net also owns: the library resolves external <c>$ref</c> targets lazily (only when
    /// an instance is evaluated), so it would miss an unsupported expression in a referenced type that
    /// never needs to be evaluated. This walker therefore traverses the graph eagerly and must be kept
    /// in sync with the dialects the library supports (Draft-07 / 2019-09 / 2020-12).
    /// </summary>
    private static Holds? HoldsKind(DraftKind draft, string keyword)
    {
        var legacy = draft == DraftKind.Draft07;
        var modern = !legacy;
        var since2020 = draft == DraftKind.Draft202012;
        return keyword switch
        {
            "not" or "additionalProperties" or "propertyNames" or "contains" => Holds.Schema,
            "if" or "then" or "else" => Holds.Schema,
            "allOf" or "anyOf" or "oneOf" => Holds.Array,
            "properties" or "patternProperties" => Holds.Map,
            "items" when since2020 => Holds.Schema,
            "items" => Holds.SchemaOrArray,
            "additionalItems" when !since2020 => Holds.Schema,
            "prefixItems" when since2020 => Holds.Array,
            "definitions" when legacy => Holds.Map,
            "dependencies" when legacy => Holds.Dependencies,
            "$defs" or "dependentSchemas" when modern => Holds.Map,
            "unevaluatedItems" or "unevaluatedProperties" or "contentSchema" when modern => Holds.Schema,
            _ => null,
        };
    }

    private sealed class Walk(Func<GtsId, JsonObject?> load)
    {
        private readonly HashSet<JsonObject> _visited = new(ReferenceEqualityComparer.Instance);
        private int _depth;

        /// <summary>
        /// Visits <paramref name="value"/> as a schema position whose local <c>$ref</c>s resolve in
        /// <paramref name="root"/>. Returns the first unsupported expression's diagnostic.
        /// </summary>
        internal string? Node(JsonNode? value, JsonObject root, DraftKind draft, string location)
        {
            if (_depth >= GtsConstants.MaxNestingDepth)
                return null;
            if (value is not JsonObject map)
                return null;
            if (!_visited.Add(map))
                return null;
            _depth++;
            try
            {
                return Scoped(map, root, draft, location);
            }
            finally
            {
                _depth--;
            }
        }

        private string? Scoped(JsonObject map, JsonObject root, DraftKind draft, string location)
        {
            // Draft-07 ignores siblings of `$ref`, but the reference target is always checked.
            if (map.TryGetPropertyValue("$ref", out var refNode) && refNode is JsonValue rv &&
                rv.TryGetValue<string>(out var reference))
            {
                var error = Reference(reference, root, draft);
                if (error is not null)
                    return error;
                if (draft == DraftKind.Draft07)
                    return null;
            }

            // The root document's `x-gts-traits-schema` is itself a schema position.
            if (ReferenceEquals(map, root) && map["x-gts-traits-schema"] is JsonObject traits)
            {
                var error = Node(traits, root, draft, Join(location, "x-gts-traits-schema"));
                if (error is not null)
                    return error;
            }

            if (map["pattern"] is JsonValue pv && pv.TryGetValue<string>(out var pattern))
            {
                var reason = GtsRegexProfile.Check(pattern);
                if (reason is not null)
                    return GtsRegexProfile.Describe(pattern, reason, Join(location, "pattern"));
            }
            if (map["patternProperties"] is JsonObject patterns)
            {
                foreach (var (expression, _) in patterns)
                {
                    var reason = GtsRegexProfile.Check(expression);
                    if (reason is not null)
                        return GtsRegexProfile.Describe(expression, reason, Join(Join(location, "patternProperties"), expression));
                }
            }

            foreach (var (keyword, child) in map)
            {
                if (HoldsKind(draft, keyword) is not { } holds)
                    continue;
                var at = Join(location, keyword);
                switch (holds, child)
                {
                    case (Holds.Schema or Holds.SchemaOrArray, JsonObject):
                        if (Node(child, root, draft, at) is { } e1)
                            return e1;
                        break;
                    case (Holds.Array or Holds.SchemaOrArray, JsonArray items):
                        for (var i = 0; i < items.Count; i++)
                            if (Node(items[i], root, draft, Join(at, i.ToString())) is { } e2)
                                return e2;
                        break;
                    case (Holds.Map or Holds.Dependencies, JsonObject members):
                        foreach (var (name, member) in members)
                            if (Node(member, root, draft, Join(at, name)) is { } e3)
                                return e3;
                        break;
                }
            }
            return null;
        }

        /// <summary>Visits the initial target of a <c>$ref</c> (local pointer or <c>gts://</c>).</summary>
        private string? Reference(string reference, JsonObject root, DraftKind draft)
        {
            if (reference.StartsWith('#'))
                return GtsJsonPointer.TryEvaluate(root, reference, out var target)
                    ? Node(target, root, draft, reference)
                    : null;

            var basePart = reference.Split('#')[0];
            var fragment = reference.Length > basePart.Length ? reference[basePart.Length..] : null;
            var id = GtsConstants.StripUriPrefix(basePart);
            if (!GtsId.TryParse(id, out var parsed) || parsed is null)
                return null;
            var document = load(parsed);
            if (document is null)
                return null;
            var targetDraft = Draft(document);
            if (string.IsNullOrEmpty(fragment) || fragment == "#")
                return Node(document, document, targetDraft, reference);
            return GtsJsonPointer.TryEvaluate(document, fragment, out var inner)
                ? Node(inner, document, targetDraft, reference)
                : null;
        }
    }

    private static string Join(string location, string segment)
    {
        var escaped = GtsJsonPointer.Escape(segment);
        return location.Contains('#') ? $"{location}/{escaped}" : $"{location}#/{escaped}";
    }
}
