namespace Gts.Store;

/// <summary>
/// Typed reason a validation, cast, or registration operation failed. Replaces the previously
/// stringly-typed <c>FailureReason</c> fields so producers and consumers share one closed set of values
/// instead of matching on magic strings. Use <see cref="GtsValidationFailures.ToWire"/> to obtain the
/// external string form.
/// </summary>
public enum GtsValidationFailure
{
    // Instance validation
    InstanceNotFound,
    NotAnInstance,
    SchemaIdMissing,
    InvalidSchemaId,
    SchemaNotFound,
    NotASchema,
    MixedDialectSchemaGraph,
    TypeSchemaValidationFailed,
    GtsRefValidationFailed,
    SchemaValidationFailed,

    /// <summary>Instance targets an <c>x-gts-abstract</c> type schema, which cannot be instantiated.</summary>
    AbstractTypeNotInstantiable,

    // Schema validation
    InvalidJsonSchema,
    InvalidGtsKeyword,
    InvalidRefFormat,
    PrecedentIncompatible,
    TraitValidationFailed,
    UnsupportedDialect,

    // Cast
    InvalidInstanceId,
    InvalidTargetSchemaId,
    TargetSchemaNotFound,
    SourceSchemaNotFound,
    NotMinorVariantPair,
    SchemaNormalizationFailed,
    CastValidationFailed,
}

/// <summary>Helpers for the external (wire) representation of <see cref="GtsValidationFailure"/>.</summary>
public static class GtsValidationFailures
{
    /// <summary>
    /// Returns the exact string historically emitted for this reason. All values map to their member
    /// name except <see cref="GtsValidationFailure.AbstractTypeNotInstantiable"/>, which predates this
    /// enum as a free-text message and is preserved verbatim so responses do not change.
    /// </summary>
    public static string ToWire(this GtsValidationFailure failure) => failure switch
    {
        GtsValidationFailure.AbstractTypeNotInstantiable => "Cannot instantiate abstract GTS Type Schema",
        _ => failure.ToString(),
    };
}
