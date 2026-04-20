using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>Validates that a derived JSON Schema remains forward-compatible with its parent in the GTS type chain.</summary>
public static class GtsSchemaDerivationValidator
{
    /// <summary>Walks the type-id chain toward the base and checks forward compatibility for each hop.</summary>
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
                errors.Add($"Invalid parent type id '{parentIdStr}'.");
                break;
            }

            var parentSchema = tryLoadTypeSchema(parentId);
            if (parentSchema is null)
            {
                errors.Add($"Parent schema '{parentIdStr}' not found.");
                break;
            }

            var parentFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(parentSchema);
            var childFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(currentSchema);

            var (ok, hopErrors) = GtsJsonSchemaEvolutionCompatibility.CheckForward(parentFlat, childFlat);
            if (!ok)
                errors.AddRange(hopErrors);

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
}
