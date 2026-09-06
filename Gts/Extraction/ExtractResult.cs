namespace Gts.Extraction;

/// <summary>
/// Result of extracting the primary entity/schema ID from a JSON object.
/// </summary>
/// <param name="Id">The effective entity or schema ID; null if none found.</param>
/// <param name="SchemaId">The schema/type ID if determined.</param>
/// <param name="SelectedEntityField">The property name from which the entity ID was taken.</param>
/// <param name="SelectedSchemaIdField">The property name from which the schema ID was taken (or derived).</param>
/// <param name="IsSchema">True if the JSON object is treated as a JSON Schema (has $schema).</param>
public sealed record ExtractResult(
    string? Id,
    string? SchemaId,
    string? SelectedEntityField,
    string? SelectedSchemaIdField,
    bool IsSchema
);
