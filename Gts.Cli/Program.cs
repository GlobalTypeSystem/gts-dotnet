using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gts;
using Gts.Application;
using Gts.Extraction;
using Gts.Store;
using Gts.Store.Validation;
using Microsoft.AspNetCore.Builder;

static class Program
{
    private static readonly JsonSerializerOptions JsonStdout = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static int _verbose;
    private static string? _path;
    private static string? _config;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 2;
        }

        ParseGlobalArgs(args, out var cmdIndex);
        ApplyEnvironmentDefaults();
        if (cmdIndex >= args.Length)
        {
            Usage();
            return 2;
        }

        var cmd = args[cmdIndex];
        var cmdArgs = cmdIndex + 1 < args.Length ? args[(cmdIndex + 1)..] : Array.Empty<string>();

        try
        {
            return cmd switch
            {
                "validate-id" => RunValidateId(cmdArgs),
                "parse-id" => RunParseId(cmdArgs),
                "match-id-pattern" => RunMatchIdPattern(cmdArgs),
                "uuid" => RunUuid(cmdArgs),
                "validate" => await RunValidateAsync(cmdArgs).ConfigureAwait(false),
                "relationships" => await RunRelationshipsAsync(cmdArgs).ConfigureAwait(false),
                "compatibility" => await RunCompatibilityAsync(cmdArgs).ConfigureAwait(false),
                "cast" => await RunCastAsync(cmdArgs).ConfigureAwait(false),
                "query" => await RunQueryAsync(cmdArgs).ConfigureAwait(false),
                "attr" => await RunAttrAsync(cmdArgs).ConfigureAwait(false),
                "list" => await RunListAsync(cmdArgs).ConfigureAwait(false),
                "server" => await RunServerAsync(cmdArgs).ConfigureAwait(false),
                "openapi" => RunOpenApi(cmdArgs),
                "version" => RunVersion(),
                "help" or "-h" or "--help" => UsageOk(),
                _ => Unknown(cmd)
            };
        }
        catch (Exception ex)
        {
            if (_verbose > 0)
                Console.Error.WriteLine(ex);
            Fatalf(ex.Message);
            return 1;
        }
    }

    private static void ApplyEnvironmentDefaults()
    {
        if (string.IsNullOrEmpty(_path))
        {
            var p = Environment.GetEnvironmentVariable("GTS_PATH");
            if (!string.IsNullOrEmpty(p))
                _path = p;
        }

        if (string.IsNullOrEmpty(_config))
        {
            var c = Environment.GetEnvironmentVariable("GTS_CONFIG");
            if (!string.IsNullOrEmpty(c))
                _config = c;
        }

        if (_verbose == 0)
        {
            var v = Environment.GetEnvironmentVariable("GTS_VERBOSE");
            if (!string.IsNullOrEmpty(v) && int.TryParse(v, out var vi))
                _verbose = vi;
        }
    }

    private static void ParseGlobalArgs(string[] args, out int cmdIndex)
    {
        cmdIndex = 0;
        while (cmdIndex < args.Length)
        {
            var a = args[cmdIndex];
            if (a is "-v" or "--verbose")
            {
                _verbose++;
                cmdIndex++;
                continue;
            }

            if (a.StartsWith("-v", StringComparison.Ordinal) && a.Length > 2 && int.TryParse(a[2..], out var vn))
            {
                _verbose += vn;
                cmdIndex++;
                continue;
            }

            if ((a is "-path" or "--path") && cmdIndex + 1 < args.Length)
            {
                _path = args[cmdIndex + 1];
                cmdIndex += 2;
                continue;
            }

            if ((a is "-config" or "--config") && cmdIndex + 1 < args.Length)
            {
                _config = args[cmdIndex + 1];
                cmdIndex += 2;
                continue;
            }

            if (a.StartsWith('-'))
            {
                cmdIndex++;
                continue;
            }

            break;
        }
    }

    private static int UsageOk()
    {
        Console.Error.Write(UsageText);
        return 0;
    }

    private static void Usage()
    {
        Console.Error.Write(UsageText);
    }

    private const string UsageText = """
GTS is a tool for working with Global Type System identifiers and schemas.

Usage:

  gts <command> [arguments]

Global options (before the command):

  -path <paths>     Comma-separated JSON / schema files or directories
  -config <file>   Optional JSON config (entity_id_fields, schema_id_fields)
  -v, --verbose    Verbose logging (repeat or use -v2)

Commands:

  validate-id       validate a GTS ID format
  parse-id          parse a GTS ID into its components
  match-id-pattern  match a GTS ID against a pattern
  uuid              deterministic UUID from a GTS ID
  validate          validate a stored instance against its schema
  relationships     resolve relationships for an entity
  compatibility     check compatibility between two schemas
  cast              cast an instance to a target schema
  query             query entities using an expression
  attr              get attribute value from a GTS entity
  list              list entities in the registry
  server            start the GTS HTTP server
  openapi           write OpenAPI specification JSON to a file
  version           print version
  help              show this message

Examples:

  gts validate-id -id gts.vendor.pkg.ns.type.v1~
  gts -path ./examples validate -id gts.vendor.pkg.ns.type.v1.0
  gts -path ./examples server -host 127.0.0.1 -port 8000

""";

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"gts: unknown command \"{cmd}\"");
        Console.Error.WriteLine("Run 'gts help' for usage.");
        return 2;
    }

    private static void Fatalf(string message)
    {
        Console.Error.WriteLine("gts: " + message);
    }

    private static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonStdout));

    private static Dictionary<string, string> ParseKvFlags(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith('-', StringComparison.Ordinal))
                continue;
            var key = a.TrimStart('-');
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-', StringComparison.Ordinal))
                d[key] = args[++i];
            else
                d[key] = "true";
        }

        return d;
    }

    private static void RequirePath()
    {
        if (string.IsNullOrWhiteSpace(_path))
            throw new InvalidOperationException("command requires -path to load entities");
    }

    private static async Task<GtsRegistry> CreateRegistryAsync(CancellationToken cancellationToken = default)
    {
        RequirePath();
        var reg = GtsRegistry.InMemoryThreadSafe(new GtsRegistryConfig(false));
        var extract = GtsRegistryBootstrap.LoadExtractOptionsFromConfig(_config);
        await GtsRegistryBootstrap.LoadIntoRegistryAsync(reg, _path, extract, cancellationToken).ConfigureAwait(false);
        if (_verbose > 0)
        {
            var n = await reg.CountAsync().ConfigureAwait(false);
            Console.Error.WriteLine($"gts: loaded path(s) '{_path}', entity count: {n}");
        }

        return reg;
    }

    private static int RunValidateId(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("usage: gts validate-id -id <gts-id>");
            return 2;
        }

        var isWildcard = id.Contains('*', StringComparison.Ordinal);
        if (isWildcard)
        {
            var pr = GtsId.TryParsePattern(id, out var pat);
            if (!pr || pat is null)
            {
                WriteJson(new
                {
                    id,
                    valid = false,
                    is_schema = false,
                    is_wildcard = true,
                    error = $"Unable to validate GTS ID '{id}': Invalid wildcard pattern"
                });
                return 0;
            }

            var isSchema = id.EndsWith("~*", StringComparison.Ordinal) || id.EndsWith(".*", StringComparison.Ordinal);
            WriteJson(new { id, valid = true, is_schema = isSchema, is_wildcard = true, error = "" });
            return 0;
        }

        if (!GtsId.TryParse(id, out var gid) || gid is null)
        {
            WriteJson(new
            {
                id,
                valid = false,
                is_schema = false,
                is_wildcard = false,
                error = $"Unable to validate GTS ID '{id}': Invalid GTS id"
            });
            return 0;
        }

        WriteJson(new { id, valid = true, is_schema = gid.IsType, is_wildcard = false, error = "" });
        return 0;
    }

    private static int RunParseId(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("usage: gts parse-id -id <gts-id>");
            return 2;
        }

        var isWildcard = id.Contains('*', StringComparison.Ordinal);
        if (isWildcard)
        {
            if (!GtsId.TryParsePattern(id, out var pat) || pat is null)
            {
                WriteJson(new
                {
                    id,
                    ok = false,
                    is_wildcard = true,
                    is_schema = false,
                    segments = (object?)null,
                    error = "Invalid pattern"
                });
                return 0;
            }

            var wildcardIsSchema = id.EndsWith(".*", StringComparison.Ordinal) || id.EndsWith("~*", StringComparison.Ordinal);
            WriteJson(new
            {
                id,
                ok = true,
                is_wildcard = true,
                is_schema = wildcardIsSchema,
                segments = pat.Segments.Select(GtsSegmentDto.FromSegment).ToList(),
                error = ""
            });
            return 0;
        }

        if (!GtsId.TryParse(id, out var gid) || gid is null)
        {
            WriteJson(new
            {
                id,
                ok = false,
                is_wildcard = false,
                is_schema = false,
                segments = (object?)null,
                error = "Parse error"
            });
            return 0;
        }

        WriteJson(new
        {
            id,
            ok = true,
            is_wildcard = false,
            is_schema = gid.IsType,
            segments = gid.Segments.Select(GtsSegmentDto.FromSegment).ToList(),
            error = ""
        });
        return 0;
    }

    private static int RunMatchIdPattern(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("pattern", out var pattern) || string.IsNullOrEmpty(pattern) ||
            !f.TryGetValue("candidate", out var candidate) || string.IsNullOrEmpty(candidate))
        {
            Console.Error.WriteLine("usage: gts match-id-pattern -pattern <pattern> -candidate <gts-id>");
            return 2;
        }

        try
        {
            if (candidate.Contains('*', StringComparison.Ordinal))
            {
                if (!GtsId.TryParsePattern(candidate, out var cPat) || cPat is null ||
                    !GtsId.TryParsePattern(pattern, out var pPat) || pPat is null)
                {
                    WriteJson(new { candidate, pattern, match = false, error = "invalid pattern" });
                    return 0;
                }

                WriteJson(new { candidate, pattern, match = cPat.Matches(pPat), error = "" });
                return 0;
            }

            if (!GtsId.TryParse(candidate, out var cId) || cId is null)
            {
                WriteJson(new { candidate, pattern, match = false, error = "invalid candidate" });
                return 0;
            }

            WriteJson(new { candidate, pattern, match = cId.Matches(pattern), error = "" });
            return 0;
        }
        catch (Exception ex)
        {
            WriteJson(new { candidate, pattern, match = false, error = ex.Message });
            return 0;
        }
    }

    private static int RunUuid(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("usage: gts uuid -id <gts-id>");
            return 2;
        }

        if (!GtsId.TryParse(id, out var gid) || gid is null)
        {
            WriteJson(new { id, uuid = "", error = "Invalid GTS id" });
            return 0;
        }

        WriteJson(new { id = gid.Id, uuid = gid.ToGuid().ToString(), error = "" });
        return 0;
    }

    private static async Task<int> RunValidateAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("id", out var instanceId) || string.IsNullOrEmpty(instanceId))
        {
            Console.Error.WriteLine("usage: gts validate -id <gts-id>");
            return 2;
        }

        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var r = await reg.ValidateInstanceAsync(instanceId).ConfigureAwait(false);
        WriteJson(new { id = instanceId, ok = r.Ok, error = r.Ok ? "" : (r.FailureReason ?? "validation failed") });
        return 0;
    }

    private static async Task<int> RunRelationshipsAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("usage: gts relationships -id <gts-id>");
            return 2;
        }

        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var graph = await GtsSchemaGraphBuilder.BuildAsync(reg, id).ConfigureAwait(false);
        WriteJson(graph);
        return 0;
    }

    private static async Task<int> RunCompatibilityAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("old", out var oldId) || string.IsNullOrEmpty(oldId) ||
            !f.TryGetValue("new", out var newId) || string.IsNullOrEmpty(newId))
        {
            Console.Error.WriteLine("usage: gts compatibility -old <old-schema-id> -new <new-schema-id>");
            return 2;
        }

        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        if (!GtsId.TryParse(oldId, out var o) || o is null || !GtsId.TryParse(newId, out var n) || n is null)
        {
            WriteJson(new
            {
                old = oldId,
                @new = newId,
                is_backward_compatible = false,
                is_forward_compatible = false,
                is_fully_compatible = false,
                backward_errors = new[] { "Invalid id" },
                forward_errors = new[] { "Invalid id" }
            });
            return 0;
        }

        var a = await reg.GetAsync(o).ConfigureAwait(false);
        var b = await reg.GetAsync(n).ConfigureAwait(false);
        if (a is null || b is null || !a.IsSchema || !b.IsSchema)
        {
            WriteJson(new
            {
                old = oldId,
                @new = newId,
                is_backward_compatible = false,
                is_forward_compatible = false,
                is_fully_compatible = false,
                backward_errors = new[] { "Schema not found" },
                forward_errors = new[] { "Schema not found" }
            });
            return 0;
        }

        var oldFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(a.Content);
        var newFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(b.Content);
        var (backOk, backErr) = GtsJsonSchemaEvolutionCompatibility.CheckBackward(oldFlat, newFlat);
        var (fwdOk, fwdErr) = GtsJsonSchemaEvolutionCompatibility.CheckForward(oldFlat, newFlat);

        WriteJson(new
        {
            old = oldId,
            @new = newId,
            is_backward_compatible = backOk,
            is_forward_compatible = fwdOk,
            is_fully_compatible = backOk && fwdOk,
            backward_errors = backErr,
            forward_errors = fwdErr
        });
        return 0;
    }

    private static async Task<int> RunCastAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("from", out var fromId) || string.IsNullOrEmpty(fromId) ||
            !f.TryGetValue("to", out var toSchemaId) || string.IsNullOrEmpty(toSchemaId))
        {
            Console.Error.WriteLine("usage: gts cast -from <instance-id> -to <to-schema-id>");
            return 2;
        }

        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var fromEntity = await reg.GetByInstanceIdAsync(fromId).ConfigureAwait(false);
        if (fromEntity is null)
        {
            WriteJson(new { error = "Instance not found" });
            return 1;
        }

        if (fromEntity.IsSchema)
        {
            WriteJson(new { error = "Source must be an instance (must be an instance)" });
            return 1;
        }

        if (!GtsId.TryParse(toSchemaId, out var toGid) || toGid is null || !toGid.IsType)
        {
            WriteJson(new { error = "Invalid target schema id" });
            return 1;
        }

        var toSchemaEntity = await reg.GetAsync(toGid).ConfigureAwait(false);
        if (toSchemaEntity is null || !toSchemaEntity.IsSchema)
        {
            WriteJson(new { error = "Target schema not found" });
            return 1;
        }

        var extract = GtsJsonEntity.ExtractId(fromEntity.Content);
        var fromSchemaIdStr = extract.SchemaId;
        if (string.IsNullOrEmpty(fromSchemaIdStr) || !GtsId.TryParse(fromSchemaIdStr, out var fromGid) || fromGid is null)
        {
            WriteJson(new { error = "Source schema not found" });
            return 1;
        }

        var fromSchemaEntity = await reg.GetAsync(fromGid).ConfigureAwait(false);
        if (fromSchemaEntity is null || !fromSchemaEntity.IsSchema)
        {
            WriteJson(new { error = "Source schema not found" });
            return 1;
        }

        var targetFlat = GtsJsonSchemaEvolutionCompatibility.FlattenSchema(toSchemaEntity.Content);
        var casted = GtsInstanceCast.CastToEffectiveSchema(fromEntity.Content, targetFlat);

        var all = await reg.GetAllAsync().ConfigureAwait(false);
        var normalizedMap = new Dictionary<GtsId, JsonObject>();
        foreach (var e in all)
        {
            if (e.IsSchema && e.GtsId is not null)
                normalizedMap[e.GtsId] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(e.Content);
        }

        if (!normalizedMap.ContainsKey(toGid))
        {
            WriteJson(new { error = "Schema normalization failed" });
            return 1;
        }

        var tolerant = (JsonObject)GtsInstanceCast.RemoveGtsConstConstraints(JsonNode.Parse(toSchemaEntity.Content.ToJsonString())!)!.AsObject();
        normalizedMap[toGid] = GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(tolerant);

        using var doc = JsonDocument.Parse(casted.ToJsonString());
        var eval = GtsJsonSchemaEvaluator.Evaluate(doc.RootElement, toGid, normalizedMap);
        if (!eval.IsValid)
        {
            WriteJson(new { error = "Cast result failed schema validation", casted_entity = JsonNode.Parse(casted.ToJsonString()) });
            return 1;
        }

        WriteJson(new
        {
            casted_entity = JsonNode.Parse(casted.ToJsonString()),
            is_backward_compatible = true,
            is_forward_compatible = true
        });
        return 0;
    }

    private static async Task<int> RunQueryAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("expr", out var expr) || string.IsNullOrEmpty(expr))
        {
            Console.Error.WriteLine("usage: gts query -expr <expression> [-limit n]");
            return 2;
        }

        var limit = f.TryGetValue("limit", out var ls) && int.TryParse(ls, out var li) ? li : 100;
        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var result = await GtsQueryEngine.ExecuteAsync(reg, expr, limit).ConfigureAwait(false);
        if (result.Error is not null)
            WriteJson(new { error = result.Error, count = 0, limit, results = Array.Empty<object>() });
        else
            WriteJson(new { error = "", count = result.Results.Count, limit, results = result.Results });
        return 0;
    }

    private static async Task<int> RunAttrAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("path", out var pathArg) || string.IsNullOrEmpty(pathArg))
        {
            Console.Error.WriteLine("usage: gts attr -path <gts-id@json.path>");
            return 2;
        }

        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var (gtsPart, pathPart) = GtsAttributeSelector.SplitGtsWithPath(pathArg);
        if (pathPart is null || string.IsNullOrWhiteSpace(gtsPart))
        {
            WriteJson(new { resolved = false });
            return 0;
        }

        if (!GtsId.TryParse(gtsPart.Trim(), out var gid) || gid is null)
        {
            WriteJson(new { resolved = false });
            return 0;
        }

        var entity = await reg.GetAsync(gid).ConfigureAwait(false);
        if (entity is null || !GtsAttributeSelector.TryResolve(entity.Content, pathPart, out var val))
        {
            WriteJson(new { resolved = false });
            return 0;
        }

        object payload = val switch
        {
            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
            JsonValue jv when jv.TryGetValue<bool>(out var b) => b,
            JsonValue jv when jv.TryGetValue<int>(out var ni) => ni,
            JsonValue jv when jv.TryGetValue<double>(out var nd) => nd,
            JsonValue jv when jv.TryGetValue<decimal>(out var nm) => nm,
            _ => JsonNode.Parse(val!.ToJsonString())!
        };
        WriteJson(new { resolved = true, value = payload });
        return 0;
    }

    private static async Task<int> RunListAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        var limit = f.TryGetValue("limit", out var ls) && int.TryParse(ls, out var li) ? li : 100;
        var reg = await CreateRegistryAsync().ConfigureAwait(false);
        var all = await reg.GetAllAsync().ConfigureAwait(false);
        var withId = all.Where(e => e.GtsId is not null).ToList();
        var total = withId.Count;
        var list = withId.Take(limit)
            .Select(e => new { id = e.GtsId!.Id, schema_id = string.IsNullOrEmpty(e.SchemaId) ? "" : e.SchemaId, is_schema = e.IsSchema })
            .ToList();
        WriteJson(new { entities = list, count = list.Count, total });
        return 0;
    }

    private static async Task<int> RunServerAsync(string[] args)
    {
        var f = ParseKvFlags(args);
        var host = f.GetValueOrDefault("host", "127.0.0.1");
        var port = f.TryGetValue("port", out var ps) && int.TryParse(ps, out var p) ? p : 8000;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://{host}:{port}");
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var reg = GtsRegistry.InMemoryThreadSafe(new GtsRegistryConfig(false));
        var extract = GtsRegistryBootstrap.LoadExtractOptionsFromConfig(_config);
        if (!string.IsNullOrWhiteSpace(_path))
            await GtsRegistryBootstrap.LoadIntoRegistryAsync(reg, _path, extract).ConfigureAwait(false);

        var app = builder.Build();
        app.MapGtsApi(reg);

        Console.WriteLine($"starting server at http://{host}:{port}");
        if (_verbose == 0)
            Console.WriteLine("use -v for verbose logging");

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static int RunOpenApi(string[] args)
    {
        var f = ParseKvFlags(args);
        if (!f.TryGetValue("out", out var outPath) || string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("usage: gts openapi -out <file> [-host address] [-port number]");
            return 2;
        }

        var host = f.GetValueOrDefault("host", "127.0.0.1");
        var port = f.TryGetValue("port", out var ps) && int.TryParse(ps, out var p) ? p : 8000;
        var spec = GtsOpenApiSpec.Build(host, port);
        File.WriteAllText(outPath, JsonSerializer.Serialize(spec, JsonStdout));
        WriteJson(new Dictionary<string, object?> { ["ok"] = true, ["out"] = outPath });
        return 0;
    }

    private static int RunVersion()
    {
        var asm = typeof(GtsId).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "unknown";
        Console.WriteLine("gts version " + info);
        if (_verbose > 0)
        {
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine("assembly " + asm.FullName);
        }

        return 0;
    }
}
