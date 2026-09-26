using System.Text.Json;
using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;
using Gts.Store;
using Microsoft.AspNetCore.Diagnostics;

namespace Gts.Application;

/// <summary>Maps the GTS HTTP API onto <see cref="WebApplication"/> (shared by <c>Gts.Server</c> and CLI <c>server</c>).</summary>
public static partial class GtsHttpApiExtensions
{
    internal static readonly GtsExtractOptions HttpExtractOptions = new()
    {
        AllowDoubleDollarKeywords = false,
        EntityIdPropertyNames =
        [
            "$id", "gtsId", "gtsIid", "gtsOid", "gtsI", "gts_id", "gts_oid", "gts_iid", "id"
        ]
    };

    public static WebApplication MapGtsApi(this WebApplication app, GtsRegistry registry)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, message) = error is JsonException or BadHttpRequestException
                ? (StatusCodes.Status422UnprocessableEntity, "Invalid JSON request body")
                : (StatusCodes.Status500InternalServerError, "Internal server error");
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = message }).ConfigureAwait(false);
        }));

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
                content = e.Content.DeepClone()
            });
        });

        app.MapPost("/entities", async (HttpRequest req) =>
        {
            var validate = string.Equals(req.Query["validate"], "true", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(req.Query["validation"], "true", StringComparison.OrdinalIgnoreCase);
            var refValidationMode = NormalizeRefValidationMode(req.Query["gts-ref-validation"]);
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { ok = false, error = "Body must be a JSON object", is_schema = false, is_type_schema = false },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var result = await GtsEntityOperations.TryAddAsync(registry, body, validate, HttpExtractOptions, refValidationMode).ConfigureAwait(false);
            if (!result.Ok)
                return Results.Json(new { ok = false, error = result.Error, is_schema = result.IsSchema, is_type_schema = result.IsSchema },
                    statusCode: result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);

            return Results.Json(new { ok = true, id = result.Id, type_id = result.SchemaId, schema_id = result.SchemaId, is_schema = result.IsSchema, is_type_schema = result.IsSchema });
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
                var result = await GtsEntityOperations.TryAddAsync(registry, body, validate: false, HttpExtractOptions).ConfigureAwait(false);
                if (!result.Ok)
                    allOk = false;
                results.Add(new { ok = result.Ok, id = result.Id, schema_id = result.SchemaId, is_schema = result.IsSchema, error = result.Error });
            }

            return Results.Json(new { ok = allOk, results });
        });

        app.MapPost("/type-schemas", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonArray schemas)
                return Results.Json(new { ok = false, error = "Request body must be a JSON array of GTS Type Schemas" }, statusCode: 422);

            var results = new List<object>();
            var allOk = true;
            foreach (var item in schemas)
            {
                if (item is not JsonObject schema)
                {
                    allOk = false;
                    results.Add(new { ok = false, type_id = (string?)null, error = "GTS Type Schema entry must be a JSON object" });
                    continue;
                }

                if (!GtsTypeSchema.TryGetDeclaredTypeId(schema, out var typeId, out var identityError))
                {
                    allOk = false;
                    results.Add(new { ok = false, type_id = (string?)null, error = identityError });
                    continue;
                }

                var result = await GtsEntityOperations.TryAddAsync(registry, schema, validate: false, HttpExtractOptions).ConfigureAwait(false);
                if (!result.Ok)
                    allOk = false;
                results.Add(new { ok = result.Ok, type_id = typeId, error = result.Error });
            }

            return Results.Json(new { ok = allOk, results });
        });

        app.MapGet("/validate-id", (string gts_id) =>
        {
            var id = gts_id ?? "";
            var isWildcard = id.Contains('*', StringComparison.Ordinal);
            if (isWildcard)
            {
                if (id.Count(c => c == '*') != 1 || !id.EndsWith('*'))
                    return Results.Json(new { id, valid = false, error = "Invalid wildcard pattern", is_wildcard = true });
                var pr = GtsId.TryParsePattern(id, out var pat);
                if (pr && pat is not null)
                    return Results.Json(new { id, valid = true, error = "", is_wildcard = true });
                return Results.Json(new { id, valid = false, error = "Invalid wildcard pattern", is_wildcard = true });
            }

            if (GtsId.TryParse(id, out var parsed) && parsed is not null && (parsed.IsType || parsed.Segments.Count > 1))
                return Results.Json(new { id, valid = true, error = "", is_wildcard = false });

            return Results.Json(new { id, valid = false, error = "Invalid GTS id", is_wildcard = false });
        });

        app.MapPost("/extract-id", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new object());
            var r = GtsJsonEntity.ExtractId(body, HttpExtractOptions);
            return Results.Json(new
            {
                id = r.Id,
                type_id = r.SchemaId,
                selected_entity_field = r.SelectedEntityField,
                selected_type_id_field = r.SelectedSchemaIdField,
                is_type_schema = r.IsSchema
            });
        });

        app.MapGet("/parse-id", (string gts_id) =>
        {
            var id = gts_id ?? "";
            var isWildcard = id.Contains('*', StringComparison.Ordinal);
            if (isWildcard)
            {
                if (id.Count(c => c == '*') == 1 && id.EndsWith('*') && GtsId.TryParsePattern(id, out var pat) && pat is not null)
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
                        is_type = wildcardIsSchema
                    });
                }

                return Results.Json(new
                {
                    id,
                    ok = false,
                    segments = Array.Empty<object>(),
                    error = "Invalid pattern",
                    is_wildcard = true,
                    is_type = false
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
                    is_type = false
                });

            return Results.Json(new
            {
                id,
                ok = true,
                segments = gid.Segments.Select(GtsSegmentDto.FromSegment).ToList(),
                error = "",
                is_wildcard = false,
                is_type = gid.IsType
            });
        });

        app.MapGet("/match-id-pattern", (string candidate, string pattern) =>
            GtsId.TryMatch(candidate, pattern, out var match)
                ? Results.Json(new { candidate, pattern, match })
                : Results.Json(new { candidate, pattern, match = false, error = "Invalid candidate or pattern" }));

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

            var r = await registry.ValidateInstanceAsync(instanceId, refValidationMode: NormalizeRefValidationMode(req.Query["gts-ref-validation"])).ConfigureAwait(false);
            if (r.Ok)
                return Results.Json(new { id = instanceId, ok = true });
            return Results.Json(new { id = instanceId, ok = false, error = InstanceError(r) ?? "validation failed" });
        });

        async Task<IResult> ValidateTypeSchema(HttpRequest req)
        {
            var requestedMode = req.Query["gts-ref-validation"].ToString();
            if (!GtsRefValidationModes.TryParse(requestedMode, out _))
                return Results.Json(new { ok = false, error = "Invalid gts-ref-validation mode" }, statusCode: 422);
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { id = "", ok = false, error = "type_id required" });
            var idNode = body["type_id"] ?? body["schema_id"];
            if (idNode is not JsonValue value || !value.TryGetValue<string>(out var typeId))
                return Results.Json(new { id = "", ok = false, error = "type_id required" });

            if (!GtsId.TryParse(typeId, out var gid) || gid is null || !gid.IsType)
                return Results.Json(new { id = typeId, ok = false, error = "Invalid GTS Type Schema ID" });

            var validation = await registry.ValidateSchemaAsync(gid, refValidationMode: NormalizeRefValidationMode(req.Query["gts-ref-validation"])).ConfigureAwait(false);
            if (!validation.Ok)
            {
                var detail = validation.Errors is { Count: > 0 }
                    ? string.Join("; ", validation.Errors)
                    : (validation.FailureReason?.ToWire() ?? "validation failed");
                return Results.Json(new { id = typeId, ok = false, error = detail });
            }

            return Results.Json(new { id = typeId, ok = true });
        }

        app.MapPost("/validate-schema", ValidateTypeSchema);
        app.MapPost("/validate-type-schema", ValidateTypeSchema);

        app.MapPost("/validate-entity", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { id = "", ok = false, error = "entity_id required" });
            if (!body.TryGetPropertyValue("entity_id", out var eid) || eid is not JsonValue ev || !ev.TryGetValue<string>(out var entityId))
                return Results.Json(new { id = "", ok = false, error = "entity_id required" });

            if (entityId.EndsWith("~", StringComparison.Ordinal))
                return await ValidateSchemaBody(entityId).ConfigureAwait(false);

            var r = await registry.ValidateInstanceAsync(entityId, refValidationMode: NormalizeRefValidationMode(req.Query["gts-ref-validation"])).ConfigureAwait(false);
            return r.Ok
                ? Results.Json(new { id = entityId, ok = true, entity_type = "instance" })
                : Results.Json(new { id = entityId, ok = false, entity_type = "instance", error = InstanceError(r) ?? "validation failed" });

            async Task<IResult> ValidateSchemaBody(string sid)
            {
                if (!GtsId.TryParse(sid, out var gid) || gid is null)
                    return Results.Json(new { id = sid, ok = false, entity_type = "schema", error = "Invalid id" });
                var vr = await registry.ValidateSchemaAsync(gid, refValidationMode: NormalizeRefValidationMode(req.Query["gts-ref-validation"])).ConfigureAwait(false);
                if (!vr.Ok)
                {
                    var detail = vr.Errors is { Count: > 0 }
                        ? string.Join("; ", vr.Errors)
                        : (vr.FailureReason?.ToWire() ?? "validation failed");
                    return Results.Json(new { id = sid, ok = false, entity_type = "schema", error = detail });
                }

                return Results.Json(new { id = sid, ok = true, entity_type = "schema" });
            }
        });

        app.MapPost("/validate-json", async (HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { detail = new[] { new { loc = new[] { "body" }, msg = "Request body must be a JSON object", type = "type_error.object" } } }, statusCode: 422);

            var entity = GtsJsonEntity.ExtractEntity(body, HttpExtractOptions);
            var id = entity.GtsId?.Id;
            if (entity.IsSchema)
            {
                if (entity.GtsId is null || !entity.GtsId.IsType)
                    return Results.Json(new { ok = false, id, type_id = entity.SchemaId, is_type_schema = true, error = "Unable to detect GTS ID in schema" });

                var validation = await registry.ValidateSchemaAsync(entity.GtsId, body).ConfigureAwait(false);
                var error = validation.Ok ? null : SchemaError(validation);
                return Results.Json(new { ok = validation.Ok, id, type_id = entity.SchemaId, is_type_schema = true, error });
            }

            var typeId = entity.SchemaId;
            if (string.IsNullOrEmpty(typeId) || !GtsId.TryParse(typeId, out var schemaId) || schemaId is null || !schemaId.IsType)
                return Results.Json(new { ok = false, id, type_id = (string?)null, is_type_schema = false, error = "Unable to determine instance type" });

            var result = await registry.ValidateJsonAsync(body, schemaId, id).ConfigureAwait(false);
            return Results.Json(new { ok = result.Ok, id, type_id = typeId, is_type_schema = false, error = InstanceError(result) });
        });

        app.MapPost("/validate-json/{*gtsType}", async (string gtsType, HttpRequest req) =>
        {
            var node = await JsonNode.ParseAsync(req.Body).ConfigureAwait(false);
            if (node is not JsonObject body)
                return Results.Json(new { detail = new[] { new { loc = new[] { "body" }, msg = "Request body must be a JSON object", type = "type_error.object" } } }, statusCode: 422);

            var typeId = Uri.UnescapeDataString(gtsType);
            if (!GtsId.TryParse(typeId, out var schemaId) || schemaId is null)
                return Results.Json(new { ok = false, id = (string?)null, type_id = typeId, is_type_schema = false, error = $"Invalid GTS Type Schema ID: '{typeId}'" });
            if (!schemaId.IsType)
                return Results.Json(new { ok = false, id = (string?)null, type_id = typeId, is_type_schema = false, error = "Registered entity must be GTS Type schema" });

            var entity = GtsJsonEntity.ExtractEntity(body, HttpExtractOptions);
            var id = entity.GtsId?.Id;
            if (entity.IsSchema)
                return Results.Json(new { ok = false, id, type_id = typeId, is_type_schema = true, error = "Explicit type validation only accepts instance JSON" });
            if (!string.IsNullOrEmpty(entity.SchemaId) && !string.Equals(entity.SchemaId, typeId, StringComparison.Ordinal))
                return Results.Json(new { ok = false, id, type_id = typeId, is_type_schema = false, error = $"Instance type '{entity.SchemaId}' does not match path type '{typeId}'" });

            var result = await registry.ValidateJsonAsync(body, schemaId, id).ConfigureAwait(false);
            return Results.Json(new { ok = result.Ok, id, type_id = typeId, is_type_schema = false, error = InstanceError(result) });
        });

        app.MapGet("/resolve-relationships", async (string gts_id) =>
        {
            var graph = await GtsSchemaGraphBuilder.BuildAsync(registry, gts_id).ConfigureAwait(false);
            return Results.Json(graph);
        });

        GtsOperationEndpoints.Map(app, registry);

        app.MapGet("/openapi", (HttpRequest req) =>
            Results.Json(GtsOpenApiSpec.Build(req.Host.Host, req.Host.Port ?? (req.IsHttps ? 443 : 80))));

        return app;
    }
}
