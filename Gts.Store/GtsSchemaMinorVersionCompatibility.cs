using System.Linq;
using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store.Validation;

namespace Gts.Store;

/// <summary>
/// Compares JSON Schema documents for two GTS type ids that differ only in the <strong>minor</strong> version
/// of the <strong>last</strong> segment. <strong>Full</strong> compatibility means the normalized schemas are
/// structurally identical (same validation behavior modulo minor identity in <c>$id</c> / <c>$ref</c>).
/// </summary>
public static class GtsSchemaMinorVersionCompatibility
{
    /// <summary>
    /// Compares two schema documents for full (bidirectional) minor-version compatibility.
    /// </summary>
    public static GtsMinorVersionCompatibilityResult CompareSchemas(
        GtsId idA,
        JsonObject schemaA,
        GtsId idB,
        JsonObject schemaB)
    {
        ArgumentNullException.ThrowIfNull(idA);
        ArgumentNullException.ThrowIfNull(idB);
        ArgumentNullException.ThrowIfNull(schemaA);
        ArgumentNullException.ThrowIfNull(schemaB);

        if (!idA.IsType || !idB.IsType)
        {
            return new GtsMinorVersionCompatibilityResult
            {
                AreCompatible = false,
                Reason = "Both identifiers must be GTS type ids (trailing ~)."
            };
        }

        if (!GtsTypeFamily.AreSameLogicalTypeMinorVariants(idA, idB))
        {
            return new GtsMinorVersionCompatibilityResult
            {
                AreCompatible = false,
                Reason =
                    "Type identifiers must match in every segment except the last, and both must specify a minor version on the last segment."
            };
        }

        var canonA = GtsSchemaMinorVersionCanonicalizer.PrepareForComparison(schemaA);
        var canonB = GtsSchemaMinorVersionCanonicalizer.PrepareForComparison(schemaB);

        if (JsonNode.DeepEquals(canonA, canonB))
            return new GtsMinorVersionCompatibilityResult { AreCompatible = true };

        return new GtsMinorVersionCompatibilityResult
        {
            AreCompatible = false,
            Reason =
                "Schemas differ after normalizing minor versions in identifiers (not fully compatible under the same validation semantics)."
        };
    }

    /// <summary>
    /// Compares two schema documents for structural (normalized identity) compatibility and for JSON Schema
    /// evolution rules: backward (old instances remain valid under the newer schema) and forward
    /// (newer instances remain valid under the older schema), using the lower minor as &quot;old&quot; and the higher as &quot;new&quot;.
    /// </summary>
    public static GtsMinorVersionPairComparison ComparePair(
        GtsId idA,
        JsonObject schemaA,
        GtsId idB,
        JsonObject schemaB)
    {
        ArgumentNullException.ThrowIfNull(idA);
        ArgumentNullException.ThrowIfNull(idB);
        ArgumentNullException.ThrowIfNull(schemaA);
        ArgumentNullException.ThrowIfNull(schemaB);

        var structural = CompareSchemas(idA, schemaA, idB, schemaB);

        if (!idA.IsType || !idB.IsType || !GtsTypeFamily.AreSameLogicalTypeMinorVariants(idA, idB))
        {
            return new GtsMinorVersionPairComparison
            {
                AreMinorVariantPair = false,
                IsStructurallyCompatible = structural.AreCompatible,
                StructuralIncompatibilityReason = structural.Reason,
                IsBackwardEvolutionCompatible = false,
                BackwardEvolutionErrors = new[] { "Not a minor-variant type pair; evolution checks were not run." },
                IsForwardEvolutionCompatible = false,
                ForwardEvolutionErrors = new[] { "Not a minor-variant type pair; evolution checks were not run." }
            };
        }

        OrderByLastMinor(idA, schemaA, idB, schemaB, out var olderId, out var olderSchema, out var newerId, out var newerSchema);

        var oldFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(olderSchema);
        var newFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(newerSchema);
        var (backOk, backErr) = GtsJsonSchemaEvolutionCompatibility.CheckBackward(oldFlat, newFlat);
        var (fwdOk, fwdErr) = GtsJsonSchemaEvolutionCompatibility.CheckForward(oldFlat, newFlat);

        return new GtsMinorVersionPairComparison
        {
            AreMinorVariantPair = true,
            OlderSchemaId = olderId,
            NewerSchemaId = newerId,
            IsStructurallyCompatible = structural.AreCompatible,
            StructuralIncompatibilityReason = structural.Reason,
            IsBackwardEvolutionCompatible = backOk,
            BackwardEvolutionErrors = backErr,
            IsForwardEvolutionCompatible = fwdOk,
            ForwardEvolutionErrors = fwdErr
        };
    }

    private static void OrderByLastMinor(
        GtsId idA,
        JsonObject schemaA,
        GtsId idB,
        JsonObject schemaB,
        out GtsId olderId,
        out JsonObject olderSchema,
        out GtsId newerId,
        out JsonObject newerSchema)
    {
        var aLast = idA.Segments.Last();
        var bLast = idB.Segments.Last();
        var am = aLast.VersionMinor!.Value;
        var bm = bLast.VersionMinor!.Value;
        if (am <= bm)
        {
            olderId = idA;
            olderSchema = schemaA;
            newerId = idB;
            newerSchema = schemaB;
        }
        else
        {
            olderId = idB;
            olderSchema = schemaB;
            newerId = idA;
            newerSchema = schemaA;
        }
    }

    /// <summary>
    /// Checks every pair of stored schemas that belong to the same minor-evolution family.
    /// Pairs that are not minor variants (e.g. <c>v1~</c> vs <c>v1.0~</c>) are skipped.
    /// </summary>
    public static GtsMinorVersionCompatibilityReport AnalyzeStoredSchemas(IEnumerable<GtsJsonEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var schemas = entities
            .Where(e => e.IsSchema && e.GtsId is not null)
            .ToList();

        var issues = new List<GtsMinorVersionPairIssue>();
        var groups = schemas.GroupBy(e => GtsTypeFamily.GetMinorEvolutionFamilyKey(e.GtsId!));

        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count < 2)
                continue;

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = members[i];
                    var b = members[j];
                    var idA = a.GtsId!;
                    var idB = b.GtsId!;

                    if (string.Equals(idA.Id, idB.Id, StringComparison.Ordinal))
                        continue;

                    if (!GtsTypeFamily.AreSameLogicalTypeMinorVariants(idA, idB))
                        continue;

                    var result = CompareSchemas(idA, a.Content, idB, b.Content);
                    if (!result.AreCompatible)
                    {
                        issues.Add(new GtsMinorVersionPairIssue
                        {
                            SchemaIdA = idA,
                            SchemaIdB = idB,
                            Reason = result.Reason ?? "Incompatible."
                        });
                    }
                }
            }
        }

        return new GtsMinorVersionCompatibilityReport
        {
            SchemaCount = schemas.Count,
            IncompatiblePairs = issues
        };
    }
}

/// <summary>Outcome of comparing two minor-version schema documents.</summary>
public sealed class GtsMinorVersionCompatibilityResult
{
    /// <summary>True when the two schemas are fully compatible (structurally equal after normalization).</summary>
    public required bool AreCompatible { get; init; }

    /// <summary>Human-readable explanation when <see cref="AreCompatible"/> is false.</summary>
    public string? Reason { get; init; }
}

/// <summary>Result of scanning many stored schemas for minor-version compatibility.</summary>
public sealed class GtsMinorVersionCompatibilityReport
{
    /// <summary>Number of schema entities considered.</summary>
    public required int SchemaCount { get; init; }

    /// <summary>Schema pairs that are minor variants but not fully compatible.</summary>
    public required IReadOnlyList<GtsMinorVersionPairIssue> IncompatiblePairs { get; init; }

    /// <summary>True when there are no incompatible minor-variant pairs.</summary>
    public bool AreAllCompatible => IncompatiblePairs.Count == 0;
}

/// <summary>One incompatible minor-variant pair.</summary>
public sealed class GtsMinorVersionPairIssue
{
    /// <summary>First schema id.</summary>
    public required GtsId SchemaIdA { get; init; }

    /// <summary>Second schema id.</summary>
    public required GtsId SchemaIdB { get; init; }

    /// <summary>Why the pair failed full compatibility.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Result of comparing two schemas for structural minor compatibility and optional evolution (backward / forward) rules.
/// </summary>
public sealed class GtsMinorVersionPairComparison
{
    /// <summary>True when both ids are type ids in the same minor-evolution family.</summary>
    public required bool AreMinorVariantPair { get; init; }

    /// <summary>When <see cref="AreMinorVariantPair"/> is true, the lower-minor schema id (evolution &quot;old&quot;).</summary>
    public GtsId? OlderSchemaId { get; init; }

    /// <summary>When <see cref="AreMinorVariantPair"/> is true, the higher-minor schema id (evolution &quot;new&quot;).</summary>
    public GtsId? NewerSchemaId { get; init; }

    /// <summary>True when normalized schema trees match (strictest notion of minor compatibility).</summary>
    public required bool IsStructurallyCompatible { get; init; }

    /// <summary>When <see cref="IsStructurallyCompatible"/> is false, a short explanation.</summary>
    public string? StructuralIncompatibilityReason { get; init; }

    /// <summary>Backward evolution: data valid under the older schema validates against the newer schema.</summary>
    public required bool IsBackwardEvolutionCompatible { get; init; }

    /// <summary>Messages from backward evolution analysis.</summary>
    public required IReadOnlyList<string> BackwardEvolutionErrors { get; init; }

    /// <summary>Forward evolution: data valid under the newer schema validates against the older schema.</summary>
    public required bool IsForwardEvolutionCompatible { get; init; }

    /// <summary>Messages from forward evolution analysis.</summary>
    public required IReadOnlyList<string> ForwardEvolutionErrors { get; init; }

    /// <summary>True when both evolution directions pass (weaker than <see cref="IsStructurallyCompatible"/>).</summary>
    public bool IsEvolutionFullyCompatible =>
        IsBackwardEvolutionCompatible && IsForwardEvolutionCompatible;
}
