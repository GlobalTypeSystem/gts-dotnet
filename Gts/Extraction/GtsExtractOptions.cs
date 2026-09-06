namespace Gts.Extraction;

/// <summary>
/// Configuration for which JSON property names to use when extracting entity and schema IDs.
/// </summary>
public sealed class GtsExtractOptions
{
    /// <summary>Default options with standard entity and schema property names.</summary>
    public static GtsExtractOptions Default { get; } = new();

    /// <summary>Property names to check for the entity ID, in priority order.</summary>
    public IReadOnlyList<string> EntityIdPropertyNames { get; init; } =
    [
        "gtsId", "gtsIid", "gtsOid", "gtsI",
        "gts_id", "gts_oid", "gts_iid", "id",
        "$$id", "$id"
    ];

    /// <summary>Property names to check for the schema/type ID, in priority order.</summary>
    public IReadOnlyList<string> SchemaIdPropertyNames { get; init; } =
    [
        "gtsTid", "gtsType", "gtsT", "gts_t", "gts_tid", "gts_type",
        "type", "schema"
    ];
}
