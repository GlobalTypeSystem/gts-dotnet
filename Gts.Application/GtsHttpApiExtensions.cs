using System.Text.Json;
using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;
using Gts.Store;
using Gts.Store.Validation;

namespace Gts.Application;

/// <summary>Maps the GTS HTTP API onto <see cref="WebApplication"/> (shared by <c>Gts.Server</c> and CLI <c>server</c>).</summary>
public static class GtsHttpApiExtensions
{
    public static WebApplication MapGtsApi(this WebApplication app, GtsRegistry registry)
    {
        app.MapGet("/entities", async (int? limit) =>
        {
            var l = limit is >= 1 and <= 1000 ? limit.Value : 100;
            var all = await registry.GetAllAsync().ConfigureAwait(false);
            var list = all.Where(e => e.GtsId is not null).Take(l)
                .Select(e => new { id = e.GtsId!.Id, schema_id = string.IsNullOrEmpty(e.SchemaId) ? null : e.SchemaId, is_schema = e.IsSchema })
                .ToList();
            return Results.Json(new { entities = list, count = list.Count, total = all.Count(e => e.GtsId is not null) });
        });

        app.MapGet("/entities/{*gtsId}", async (string gtsId) =>
        {
            var id = Uri.UnescapeDataString(gtsId.Trim());
            GtsJsonEntity? e = await registry.GetByInstanceIdAsync(id).ConfigureAwait(false);
            if (e is null && GtsId.TryParse(id, out var gid) && gid is not null)
                e = await registry.GetAsync(gid).ConfigureAwait(false);
            if (e is null)
                return Results.Json(new { ok = false, error = $"Entity '{id}' not found" });

            return Results.Json(new
            {
                ok = true,
                id = e.GtsId?.Id ?? id,
                schema_id = string.IsNullOrEmpty(e.SchemaId) ? null : e.SchemaId,
                is_schema = e.IsSchema,
                content = JsonNode.Parse(e.Content.ToJsonString())
            });
        });

        app.MapPost("/entities", async (HttpRequest req) =>
        {
            var validate = string.Equals(req.Query["validate"], "true", StringComparison.OrdinalIgnoreCase);
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { ok = false, error = "Body must be a JSON object", is_schema = false },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            GtsJsonKeyNormalizer.Apply(body);
            var result = await GtsEntityOperations.TryAddAsync(registry, body, validate).ConfigureAwait(false);
            if (!result.Ok)
                return Results.Json(new { ok = false, error = result.Error, is_schema = result.IsSchema },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            return Results.Json(new { ok = true, id = result.Id, schema_id = result.SchemaId, is_schema = result.IsSchema });
        });

        app.MapPost("/entities/bulk", async (HttpRequest req) =>
        {
            var arr = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false) as JsonArray;
            if (arr is null)
                return Results.Json(new { ok = false, results = Array.Empty<object>() });

            var results = new List<object>();
            var allOk = true;
            foreach (var item in arr)
            {
                if (item is not JsonObject body)
                    continue;
                GtsJsonKeyNormalizer.Apply(body);
                var result = await GtsEntityOperations.TryAddAsync(registry, body, validate: false).ConfigureAwait(false);
                if (!result.Ok)
                    allOk = false;
                results.Add(new { ok = result.Ok, id = result.Id, schema_id = result.SchemaId, is_schema = result.IsSchema, error = result.Error });
            }

            return Results.Json(new { ok = allOk, results });
        });

        app.MapPost("/schemas", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body || !body.TryGetPropertyValue("type_id", out var tid) ||
                tid is not JsonValue tv || !tv.TryGetValue<string>(out var typeId))
                return Results.Json(new { ok = false, error = "type_id required" }, statusCode: 422);

            if (!body.TryGetPropertyValue("schema", out var schemaNode) || schemaNode is not JsonObject schemaObj)
                return Results.Json(new { ok = false, error = "schema required" }, statusCode: 422);

            var wrapped = (JsonObject)schemaObj.DeepClone()!;
            var gtsUri = "gts://" + typeId.Trim();
            wrapped["$id"] = gtsUri;
            if (!wrapped.TryGetPropertyValue("$schema", out _))
                wrapped["$schema"] = "http://json-schema.org/draft-07/schema#";

            GtsJsonKeyNormalizer.Apply(wrapped);
            var result = await GtsEntityOperations.TryAddAsync(registry, wrapped, validate: false).ConfigureAwait(false);
            if (!result.Ok)
                return Results.Json(new { ok = false, error = result.Error }, statusCode: 422);

            return Results.Json(new { ok = true, id = typeId });
        });

        app.MapGet("/validate-id", (string gts_id) =>
        {
            var id = gts_id ?? "";
            var isWildcard = id.Contains('*', StringComparison.Ordinal);
            if (isWildcard)
            {
                var pr = GtsId.TryParsePattern(id, out var pat);
                if (pr && pat is not null)
                    return Results.Json(new { id, valid = true, error = "", is_wildcard = true });
                return Results.Json(new { id, valid = false, error = "Invalid wildcard pattern", is_wildcard = true });
            }

            if (GtsId.TryParse(id, out var _))
                return Results.Json(new { id, valid = true, error = "", is_wildcard = false });

            return Results.Json(new { id, valid = false, error = "Invalid GTS id", is_wildcard = false });
        });

        app.MapPost("/extract-id", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new object());
            GtsJsonKeyNormalizer.Apply(body);
            var r = GtsJsonEntity.ExtractId(body);
            return Results.Json(new
            {
                id = r.Id,
                schema_id = r.SchemaId,
                selected_entity_field = r.SelectedEntityField,
                selected_schema_id_field = r.SelectedSchemaIdField,
                is_schema = r.IsSchema
            });
        });

        app.MapGet("/parse-id", (string gts_id) =>
        {
            var id = gts_id ?? "";
            var isWildcard = id.Contains('*', StringComparison.Ordinal);
            if (isWildcard)
            {
                if (GtsId.TryParsePattern(id, out var pat) && pat is not null)
                {
                    var wildcardIsSchema = id.EndsWith(".*", StringComparison.Ordinal) ||
                                           id.EndsWith("~*", StringComparison.Ordinal);
                    return Results.Json(new
                    {
                        id,
                        ok = true,
                        segments = pat.Segments.Select(GtsSegmentDto.FromSegment).ToList(),
                        error = "",
                        is_wildcard = true,
                        is_schema = wildcardIsSchema
                    });
                }

                return Results.Json(new
                {
                    id,
                    ok = false,
                    segments = Array.Empty<object>(),
                    error = "Invalid pattern",
                    is_wildcard = true,
                    is_schema = false
                });
            }

            if (!GtsId.TryParse(id, out var gid) || gid is null)
                return Results.Json(new
                {
                    id,
                    ok = false,
                    segments = Array.Empty<object>(),
                    error = "Parse error",
                    is_wildcard = false,
                    is_schema = false
                });

            return Results.Json(new
            {
                id,
                ok = true,
                segments = gid.Segments.Select(GtsSegmentDto.FromSegment).ToList(),
                error = "",
                is_wildcard = false,
                is_schema = gid.IsType
            });
        });

        app.MapGet("/match-id-pattern", (string candidate, string pattern) =>
        {
            try
            {
                if (candidate.Contains('*', StringComparison.Ordinal))
                {
                    if (!GtsId.TryParsePattern(candidate, out var cPat) || cPat is null ||
                        !GtsId.TryParsePattern(pattern, out var pPat) || pPat is null)
                        return Results.Json(new { candidate, pattern, match = false, error = "invalid pattern" });
                    return Results.Json(new { candidate, pattern, match = cPat.Matches(pPat) });
                }

                if (!GtsId.TryParse(candidate, out var cId) || cId is null)
                    return Results.Json(new { candidate, pattern, match = false, error = "invalid candidate" });
                return Results.Json(new { candidate, pattern, match = cId.Matches(pattern) });
            }
            catch (Exception ex)
            {
                return Results.Json(new { candidate, pattern, match = false, error = ex.Message });
            }
        });

        app.MapGet("/uuid", (string gts_id) =>
        {
            if (!GtsId.TryParse(gts_id, out var id) || id is null)
                return Results.Json(new { id = gts_id, uuid = "" });
            return Results.Json(new { id = id.Id, uuid = id.ToGuid().ToString() });
        });

        app.MapPost("/validate-instance", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { id = "", ok = false, error = "instance_id required" });
            if (!body.TryGetPropertyValue("instance_id", out var iid) || iid is not JsonValue jv ||
                !jv.TryGetValue<string>(out var instanceId))
                return Results.Json(new { id = "", ok = false, error = "instance_id required" });

            var r = await registry.ValidateInstanceAsync(instanceId).ConfigureAwait(false);
            if (r.Ok)
                return Results.Json(new { id = instanceId, ok = true });
            return Results.Json(new { id = instanceId, ok = false, error = r.FailureReason ?? "validation failed" });
        });

        app.MapPost("/validate-schema", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { id = "", ok = false, error = "schema_id required" });
            if (!body.TryGetPropertyValue("schema_id", out var sid) || sid is not JsonValue sv || !sv.TryGetValue<string>(out var schemaId))
                return Results.Json(new { id = "", ok = false, error = "schema_id required" });

            if (!GtsId.TryParse(schemaId, out var gid) || gid is null || !gid.IsType)
                return Results.Json(new { id = schemaId, ok = false, error = "Invalid schema id" });

            var entity = await registry.GetAsync(gid).ConfigureAwait(false);
            if (entity is null || !entity.IsSchema)
                return Results.Json(new { id = schemaId, ok = false, error = "Schema not found" });

            JsonObject? LoadSchema(GtsId id)
            {
                var t = registry.GetAsync(id).AsTask().GetAwaiter().GetResult();
                return t?.IsSchema == true ? t.Content : null;
            }

            try
            {
                GtsSchemaRefFormatValidator.ValidateRefs(entity.Content);
                var (ok, errs) = GtsSchemaDerivationValidator.ValidateAgainstRegistry(gid, entity.Content, LoadSchema);
                if (!ok)
                    return Results.Json(new { id = schemaId, ok = false, error = string.Join("; ", errs) });
                return Results.Json(new { id = schemaId, ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { id = schemaId, ok = false, error = ex.Message });
            }
        });

        app.MapPost("/validate-entity", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { id = "", ok = false, error = "entity_id required" });
            if (!body.TryGetPropertyValue("entity_id", out var eid) || eid is not JsonValue ev || !ev.TryGetValue<string>(out var entityId))
                return Results.Json(new { id = "", ok = false, error = "entity_id required" });

            if (entityId.EndsWith("~", StringComparison.Ordinal))
                return await ValidateSchemaBody(entityId).ConfigureAwait(false);

            var r = await registry.ValidateInstanceAsync(entityId).ConfigureAwait(false);
            return r.Ok
                ? Results.Json(new { id = entityId, ok = true })
                : Results.Json(new { id = entityId, ok = false, error = r.FailureReason ?? "validation failed" });

            async Task<IResult> ValidateSchemaBody(string sid)
            {
                if (!GtsId.TryParse(sid, out var gid) || gid is null)
                    return Results.Json(new { id = sid, ok = false, error = "Invalid id" });
                var entity = await registry.GetAsync(gid).ConfigureAwait(false);
                if (entity is null || !entity.IsSchema)
                    return Results.Json(new { id = sid, ok = false, error = "Schema not found" });

                JsonObject? LoadSchema(GtsId id)
                {
                    var t = registry.GetAsync(id).AsTask().GetAwaiter().GetResult();
                    return t?.IsSchema == true ? t.Content : null;
                }

                try
                {
                    GtsSchemaRefFormatValidator.ValidateRefs(entity.Content);
                    var (ok, errs) = GtsSchemaDerivationValidator.ValidateAgainstRegistry(gid, entity.Content, LoadSchema);
                    return ok
                        ? Results.Json(new { id = sid, ok = true })
                        : Results.Json(new { id = sid, ok = false, error = string.Join("; ", errs) });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { id = sid, ok = false, error = ex.Message });
                }
            }
        });

        app.MapGet("/resolve-relationships", async (string gts_id) =>
        {
            var graph = await GtsSchemaGraphBuilder.BuildAsync(registry, gts_id).ConfigureAwait(false);
            return Results.Json(graph);
        });

        app.MapGet("/compatibility", async (string old_schema_id, string new_schema_id) =>
        {
            if (!GtsId.TryParse(old_schema_id, out var o) || o is null || !GtsId.TryParse(new_schema_id, out var n) || n is null)
                return Results.Json(new
                {
                    old = old_schema_id,
                    @new = new_schema_id,
                    is_backward_compatible = false,
                    is_forward_compatible = false,
                    is_fully_compatible = false,
                    backward_errors = new[] { "Invalid id" },
                    forward_errors = new[] { "Invalid id" }
                });

            var a = await registry.GetAsync(o).ConfigureAwait(false);
            var b = await registry.GetAsync(n).ConfigureAwait(false);
            if (a is null || b is null || !a.IsSchema || !b.IsSchema)
                return Results.Json(new
                {
                    old = old_schema_id,
                    @new = new_schema_id,
                    is_backward_compatible = false,
                    is_forward_compatible = false,
                    is_fully_compatible = false,
                    backward_errors = new[] { "Schema not found" },
                    forward_errors = new[] { "Schema not found" }
                });

            var oldFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(a.Content);
            var newFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(b.Content);
            var (backOk, backErr) = GtsJsonSchemaEvolutionCompatibility.CheckBackward(oldFlat, newFlat);
            var (fwdOk, fwdErr) = GtsJsonSchemaEvolutionCompatibility.CheckForward(oldFlat, newFlat);

            return Results.Json(new
            {
                old = old_schema_id,
                @new = new_schema_id,
                is_backward_compatible = backOk,
                is_forward_compatible = fwdOk,
                is_fully_compatible = backOk && fwdOk,
                backward_errors = backErr,
                forward_errors = fwdErr
            });
        });

        app.MapPost("/cast", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { error = "instance_id required" });
            if (!body.TryGetPropertyValue("instance_id", out var i) || i is not JsonValue iv || !iv.TryGetValue<string>(out var instanceId))
                return Results.Json(new { error = "instance_id required" });
            if (!body.TryGetPropertyValue("to_schema_id", out var t) || t is not JsonValue tv || !tv.TryGetValue<string>(out var toSchemaId))
                return Results.Json(new { error = "to_schema_id required" });

            var fromEntity = await registry.GetByInstanceIdAsync(instanceId).ConfigureAwait(false);
            if (fromEntity is null)
                return Results.Json(new { error = "Instance not found" });
            if (fromEntity.IsSchema)
                return Results.Json(new { error = "Source must be an instance (must be an instance)" });

            if (!GtsId.TryParse(toSchemaId, out var toGid) || toGid is null || !toGid.IsType)
                return Results.Json(new { error = "Invalid target schema id" });

            var toSchemaEntity = await registry.GetAsync(toGid).ConfigureAwait(false);
            if (toSchemaEntity is null || !toSchemaEntity.IsSchema)
                return Results.Json(new { error = "Target schema not found" });

            var extract = GtsJsonEntity.ExtractId(fromEntity.Content);
            var fromSchemaIdStr = extract.SchemaId;
            if (string.IsNullOrEmpty(fromSchemaIdStr) || !GtsId.TryParse(fromSchemaIdStr, out var fromGid) || fromGid is null)
                return Results.Json(new { error = "Source schema not found" });

            var fromSchemaEntity = await registry.GetAsync(fromGid).ConfigureAwait(false);
            if (fromSchemaEntity is null || !fromSchemaEntity.IsSchema)
                return Results.Json(new { error = "Source schema not found" });

            var targetFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(toSchemaEntity.Content);
            var casted = GtsInstanceCast.CastToEffectiveSchema(fromEntity.Content, targetFlat);

            var all = await registry.GetAllAsync().ConfigureAwait(false);
            var normalizedMap = new Dictionary<GtsId, JsonObject>();
            foreach (var e in all)
            {
                if (e.IsSchema && e.GtsId is not null)
                    normalizedMap[e.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(e.Content);
            }

            if (!normalizedMap.ContainsKey(toGid))
                return Results.Json(new { error = "Schema normalization failed" });

            var tolerant = (JsonObject)GtsInstanceCast.RemoveGtsConstConstraints(JsonNode.Parse(toSchemaEntity.Content.ToJsonString())!)!.AsObject();
            normalizedMap[toGid] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(tolerant);

            using var doc = JsonDocument.Parse(casted.ToJsonString());
            var eval = GtsJsonSchemaEvaluator.Evaluate(doc.RootElement, toGid, normalizedMap);
            if (!eval.IsValid)
                return Results.Json(new { error = "Cast result failed schema validation", casted_entity = JsonNode.Parse(casted.ToJsonString()) });

            return Results.Json(new
            {
                casted_entity = JsonNode.Parse(casted.ToJsonString()),
                is_backward_compatible = true,
                is_forward_compatible = true
            });
        });

        app.MapGet("/query", async (string expr, int? limit) =>
        {
            var lim = limit is >= 1 and <= 1000 ? limit!.Value : 100;
            var result = await GtsQueryEngine.ExecuteAsync(registry, expr, lim).ConfigureAwait(false);
            if (result.Error is not null)
                return Results.Json(new { error = result.Error, results = Array.Empty<object>(), limit = lim });
            return Results.Json(new { results = result.Results, count = result.Results.Count, limit = lim });
        });

        app.MapGet("/attr", async (string gts_with_path) =>
        {
            var (gtsPart, pathPart) = GtsAttributeSelector.SplitGtsWithPath(gts_with_path);
            if (pathPart is null)
                return Results.Json(new { resolved = false });

            if (string.IsNullOrWhiteSpace(gtsPart))
                return Results.Json(new { resolved = false });

            if (!GtsId.TryParse(gtsPart.Trim(), out var gid) || gid is null)
                return Results.Json(new { resolved = false });

            var entity = await registry.GetAsync(gid).ConfigureAwait(false);
            if (entity is null)
                return Results.Json(new { resolved = false });

            if (!GtsAttributeSelector.TryResolve(entity.Content, pathPart, out var val))
                return Results.Json(new { resolved = false });

            return val switch
            {
                JsonValue jv when jv.TryGetValue<string>(out var s) => Results.Json(new { resolved = true, value = s }),
                JsonValue jv when jv.TryGetValue<bool>(out var b) => Results.Json(new { resolved = true, value = b }),
                JsonValue jv when jv.TryGetValue<int>(out var ni) => Results.Json(new { resolved = true, value = ni }),
                JsonValue jv when jv.TryGetValue<double>(out var nd) => Results.Json(new { resolved = true, value = nd }),
                JsonValue jv when jv.TryGetValue<decimal>(out var nm) => Results.Json(new { resolved = true, value = nm }),
                _ => Results.Json(new { resolved = true, value = JsonNode.Parse(val!.ToJsonString()) })
            };
        });

        return app;
    }
}
