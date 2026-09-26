using System.Text.Json.Nodes;
using Json.Pointer;

namespace Gts.Store.Validation;

internal static class GtsJsonPointer
{
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