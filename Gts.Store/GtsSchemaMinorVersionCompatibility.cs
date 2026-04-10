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
