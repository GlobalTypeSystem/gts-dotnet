using System.Text.Json.Nodes;

namespace Gts.Application;

/// <summary>Normalizes <c>$$</c>-prefixed JSON keys to <c>$</c> keys (same as HTTP ingestion).</summary>
public static class GtsJsonKeyNormalizer
{
    public static void Apply(JsonObject root) => Walk(root);

    private static void Walk(JsonObject o)
    {
        foreach (var key in o.Select(kv => kv.Key).ToList())
        {
            if (key.StartsWith("$$", StringComparison.Ordinal))
            {
                var nk = "$" + key[2..];
                o[nk] = o[key]!.DeepClone();
                o.Remove(key);
            }
        }

        foreach (var (_, v) in o.ToList())
        {
            switch (v)
            {
                case JsonObject jo:
                    Walk(jo);
                    break;
                case JsonArray ja:
                    foreach (var x in ja)
                    {
                        if (x is JsonObject j2)
                            Walk(j2);
                    }

                    break;
            }
        }
    }
}
