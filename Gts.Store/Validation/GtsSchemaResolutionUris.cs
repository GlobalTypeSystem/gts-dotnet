namespace Gts.Store.Validation;

/// <summary>
/// Maps GTS schema identifiers to absolute HTTPS URIs for JSON Schema tooling.
/// .NET's <see cref="Uri"/> rejects <c>gts://</c> identifiers that contain <c>~</c> in the host,
/// so evaluation uses a stable synthetic base and percent-encoded paths.
/// </summary>
internal static class GtsSchemaResolutionUris
{
    internal const string SyntheticBase = "https://gts.json-schema.invalid/";

    internal static Uri ToSyntheticUri(string gtsTypeId)
    {
        return new Uri(SyntheticBase + Uri.EscapeDataString(gtsTypeId));
    }

    internal static bool TryGetGtsId(Uri uri, out string gtsId)
    {
        gtsId = "";
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var abs = uri.AbsoluteUri;
        if (!abs.StartsWith(SyntheticBase, StringComparison.Ordinal))
            return false;

        var encoded = abs.AsSpan(SyntheticBase.Length);
        if (encoded.IsEmpty)
            return false;

        gtsId = Uri.UnescapeDataString(encoded.ToString());
        return gtsId.Length > 0;
    }
}
