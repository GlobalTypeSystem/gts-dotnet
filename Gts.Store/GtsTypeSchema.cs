using System.Text.Json.Nodes;

namespace Gts.Store;

/// <summary>
/// The canonical-identity and JSON Schema dialect rules a GTS Type Schema must satisfy
/// (README §2.4, §11.0), in one place so every ingest and validation route gives the same
/// verdict on the same document. Mirrors the single <c>declared_type_id</c> / <c>document_dialect</c>
/// entry points in the Rust reference implementation.
/// </summary>
public static class GtsTypeSchema
{
    private const string GtsUriPrefix = GtsConstants.UriPrefix;

    /// <summary>
    /// Resolves the JSON Schema dialect a Type Schema declares in its top-level <c>$schema</c>.
    /// GTS admits Draft-07, Draft 2019-09 and Draft 2020-12; Draft-07 is the floor and there is no
    /// custom meta-schema. The <c>http</c>/<c>https</c> spellings and a trailing <c>#</c> are equivalent;
    /// anything else is refused rather than read as some fallback dialect.
    /// </summary>
    /// <param name="schema">The Type Schema document.</param>
    /// <param name="dialect">On success, the canonical short tag (<c>draft-07</c>, <c>2019-09</c>, <c>2020-12</c>).</param>
    /// <param name="error">On failure, why the document declares no supported dialect.</param>
    public static bool TryGetSupportedDialect(JsonObject schema, out string dialect, out string? error)
    {
        ArgumentNullException.ThrowIfNull(schema);
        dialect = "";
        error = null;
        if (!TryGetNonEmptyString(schema, "$schema", out var declared))
        {
            error = "a GTS Type Schema must declare a top-level '$schema'";
            return false;
        }

        var normalized = declared.Replace("https://", "http://", StringComparison.Ordinal).TrimEnd('#');
        switch (normalized)
        {
            case "http://json-schema.org/draft-07/schema":
                dialect = "draft-07";
                return true;
            case "http://json-schema.org/draft/2019-09/schema":
                dialect = "2019-09";
                return true;
            case "http://json-schema.org/draft/2020-12/schema":
                dialect = "2020-12";
                return true;
            default:
                error = IsPreDraft07(normalized)
                    ? $"'$schema' declares '{declared}', but Draft-07 is the minimum supported dialect"
                    : $"'$schema' declares '{declared}', which is not a supported dialect "
                      + "(Draft-07, Draft 2019-09 or Draft 2020-12)";
                return false;
        }
    }

    /// <summary>
    /// The GTS Type Identifier a canonical Type Schema declares: a top-level <c>$schema</c> and a
    /// top-level <c>$id</c> of the form <c>gts://&lt;type-id&gt;</c> naming a GTS type. Whether the
    /// dialect is supported is a validation question, not an identity one, so it is not checked here.
    /// </summary>
    /// <param name="schema">The candidate Type Schema document.</param>
    /// <param name="typeId">On success, the bare GTS Type Identifier (no <c>gts://</c> prefix).</param>
    /// <param name="error">On failure, why the document is not a canonical GTS Type Schema.</param>
    public static bool TryGetDeclaredTypeId(JsonObject schema, out string typeId, out string? error)
    {
        ArgumentNullException.ThrowIfNull(schema);
        typeId = "";
        error = null;
        if (!schema.ContainsKey("$schema"))
        {
            error = "a GTS Type Schema must declare a top-level '$schema'";
            return false;
        }

        if (!TryGetNonEmptyString(schema, "$id", out var id) || !id.StartsWith(GtsUriPrefix, StringComparison.Ordinal))
        {
            error = $"a GTS Type Schema must declare a top-level '$id' of the form '{GtsUriPrefix}<type-id>'";
            return false;
        }

        var candidate = id[GtsUriPrefix.Length..];
        if (!GtsId.TryParse(candidate, out var parsed) || parsed is null || !parsed.IsType)
        {
            error = $"'$id' '{id}' does not name a GTS Type Identifier";
            return false;
        }

        typeId = candidate;
        return true;
    }

    private static bool IsPreDraft07(string normalizedSchemaUri) => normalizedSchemaUri is
        "http://json-schema.org/draft-06/schema" or
        "http://json-schema.org/draft-04/schema" or
        "http://json-schema.org/draft-03/schema" or
        "http://json-schema.org/draft-02/schema" or
        "http://json-schema.org/draft-01/schema" or
        "http://json-schema.org/draft-00/schema";

    private static bool TryGetNonEmptyString(JsonObject obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetPropertyValue(name, out var node) || node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var parsed) || string.IsNullOrEmpty(parsed))
            return false;
        value = parsed;
        return true;
    }
}
