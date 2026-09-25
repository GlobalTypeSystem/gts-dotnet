namespace Gts.Store;

/// <summary>
/// Names of the GTS-specific JSON Schema extension keywords (GTS spec §9). Centralized so the keyword
/// spellings live in one place instead of being repeated as string literals across the validators.
/// </summary>
public static class GtsSchemaKeywords
{
    /// <summary>Common prefix shared by every GTS schema extension keyword.</summary>
    public const string Prefix = "x-gts-";

    /// <summary><c>x-gts-ref</c>: constrains a string value to a GTS identifier matching a pattern (§9.6).</summary>
    public const string Ref = "x-gts-ref";

    /// <summary><c>x-gts-final</c>: marks a type schema as non-derivable.</summary>
    public const string Final = "x-gts-final";

    /// <summary><c>x-gts-abstract</c>: marks a type schema as non-instantiable.</summary>
    public const string Abstract = "x-gts-abstract";

    /// <summary><c>x-gts-traits-schema</c>: declares the trait schema for a type and its descendants (§9.7).</summary>
    public const string TraitsSchema = "x-gts-traits-schema";

    /// <summary><c>x-gts-traits</c>: supplies concrete values for declared trait properties (§9.7).</summary>
    public const string Traits = "x-gts-traits";
}
