using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Application;

/// <summary>Builds a nested JSON graph of GTS references (same shape as <c>/resolve-relationships</c>).</summary>
public static class GtsSchemaGraphBuilder
{
    public static async Task<JsonObject> BuildAsync(GtsRegistry registry, string gtsId, CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return await Node(registry, gtsId, seen).ConfigureAwait(false);

        async Task<JsonObject> Node(GtsRegistry reg, string id, HashSet<string> seenSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ret = new JsonObject { ["id"] = id };
            if (!seenSet.Add(id))
                return ret;

            GtsJsonEntity? entity = null;
            if (GtsId.TryParse(id, out var gid) && gid is not null)
                entity = await reg.GetAsync(gid).ConfigureAwait(false);
            if (entity is null)
                entity = await reg.GetByInstanceIdAsync(id).ConfigureAwait(false);

            if (entity is null)
            {
                ret["errors"] = "Entity not found";
                return ret;
            }

            var refsObj = new JsonObject();
            foreach (var r in entity.GtsRefs)
            {
                if (r.Id == id)
                    continue;
                if (r.Id.StartsWith("http://json-schema.org", StringComparison.Ordinal) ||
                    r.Id.StartsWith("https://json-schema.org", StringComparison.Ordinal))
                    continue;
                refsObj[r.SourcePath] = await Node(reg, r.Id, seenSet).ConfigureAwait(false);
            }

            if (refsObj.Count > 0)
                ret["refs"] = refsObj;

            if (!string.IsNullOrEmpty(entity.SchemaId) &&
                !entity.SchemaId.StartsWith("http://json-schema.org", StringComparison.Ordinal) &&
                !entity.SchemaId.StartsWith("https://json-schema.org", StringComparison.Ordinal))
                ret["schema_id"] = await Node(reg, entity.SchemaId, seenSet).ConfigureAwait(false);

            return ret;
        }
    }
}
