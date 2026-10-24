using System.Text.Json.Nodes;
using Json.Pointer;

namespace Gts.Store.Validation;

internal static class GtsJsonPointer
{
    /// <summary>Escapes a single reference token for use in a JSON Pointer (<c>~</c>→<c>~0</c>, <c>/</c>→<c>~1</c>).</summary>
    internal static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    internal static bool TryEvaluate(JsonNode? root, string reference, out JsonNode? result)
    {
        result = null;
        if (root is null || reference == "#")
        {
            result = root;
            return root is not null;
        }

        var pointerText = reference.StartsWith('#') ? reference[1..] : reference;
        return JsonPointer.TryParse(pointerText, out var pointer) && pointer.TryEvaluate(root, out result);
    }
}
