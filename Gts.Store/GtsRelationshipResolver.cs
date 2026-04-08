using Gts.Extraction;

namespace Gts.Store;

/// <summary>
/// Loads a snapshot of entities and checks that every concrete GTS reference points at a stored schema (type id) or instance.
/// </summary>
public static class GtsRelationshipResolver
{
    /// <summary>
    /// Analyzes <paramref name="entities"/> for missing schema and instance targets. Pattern identifiers (wildcards) are ignored.
    /// </summary>
    public static GtsRelationshipResolutionResult Analyze(IList<GtsJsonEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var schemasById = new Dictionary<GtsId, GtsJsonEntity>();
        var instanceKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in entities)
        {
            if (e.IsSchema && e.GtsId is not null)
                schemasById[e.GtsId] = e;

            var ex = GtsJsonEntity.ExtractId(e.Content);
            if (!string.IsNullOrEmpty(ex.Id))
                instanceKeys.Add(ex.Id);
        }

        var schemaCount = entities.Count(e => e.IsSchema);
        var instanceCount = entities.Count - schemaCount;

        var broken = new List<GtsBrokenReference>();

        foreach (var entity in entities)
        {
            var sourceId = ResolveSourceId(entity);
            foreach (var r in entity.GtsRefs)
            {
                if (IsPatternReference(r.Id))
                    continue;

                if (!GtsId.TryParse(r.Id, out var target) || target is null)
                {
                    if (GtsId.TryParsePattern(r.Id, out var pat) && pat is { IsPattern: true })
                        continue;
                    broken.Add(new GtsBrokenReference(
                        sourceId,
                        entity.IsSchema,
                        r.Id,
                        r.SourcePath,
                        "InvalidGtsId"));
                    continue;
                }

                if (target.IsType)
                {
                    if (!schemasById.ContainsKey(target))
                    {
                        broken.Add(new GtsBrokenReference(
                            sourceId,
                            entity.IsSchema,
                            r.Id,
                            r.SourcePath,
                            "MissingSchema"));
                    }
                }
                else
                {
                    if (!instanceKeys.Contains(target.Id))
                    {
                        broken.Add(new GtsBrokenReference(
                            sourceId,
                            entity.IsSchema,
                            r.Id,
                            r.SourcePath,
                            "MissingInstance"));
                    }
                }
            }
        }

        foreach (var entity in entities)
        {
            if (entity.IsSchema)
                continue;

            var ex = GtsJsonEntity.ExtractId(entity.Content);
            if (string.IsNullOrEmpty(ex.SchemaId) || !ex.SchemaId.EndsWith("~", StringComparison.Ordinal))
                continue;

            if (!GtsId.TryParse(ex.SchemaId, out var schemaGtsId) || schemaGtsId is null || !schemaGtsId.IsType)
                continue;

            if (schemasById.ContainsKey(schemaGtsId))
                continue;

            if (entity.GtsRefs.Any(r => string.Equals(r.Id, ex.SchemaId, StringComparison.Ordinal)))
                continue;

            var sourceId = ResolveSourceId(entity);
            broken.Add(new GtsBrokenReference(
                sourceId,
                false,
                ex.SchemaId,
                "(type binding)",
                "MissingTypeBinding"));
        }

        return new GtsRelationshipResolutionResult
        {
            EntityCount = entities.Count,
            SchemaCount = schemaCount,
            InstanceCount = instanceCount,
            BrokenReferences = broken
        };
    }

    private static string ResolveSourceId(GtsJsonEntity entity)
    {
        if (entity.GtsId is not null)
            return entity.GtsId.Id;

        var ex = GtsJsonEntity.ExtractId(entity.Content);
        return string.IsNullOrEmpty(ex.Id) ? "<unknown>" : ex.Id;
    }

    private static bool IsPatternReference(string refId)
    {
        // Concrete type/instance strings may also parse as "patterns" in the grammar; still validate them.
        if (GtsId.TryParse(refId, out _))
            return false;

        if (refId.Contains('*', StringComparison.Ordinal))
            return true;

        return GtsId.TryParsePattern(refId, out var p) && p is { IsPattern: true };
    }
}
