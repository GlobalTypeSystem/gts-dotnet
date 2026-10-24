> Status: initial draft v0.1, not for production use

# GTS .NET Library

An idiomatic C#/.NET library for working with **GTS** ([Global Type System](https://github.com/GlobalTypeSystem/gts-spec)) identifiers and JSON/JSON Schema artifacts.

Supported GTS spec version: `v0.17.0`

The conformance image is pinned to the released `v0.17.0` in [`.gts-spec-version`](.gts-spec-version).

## Roadmap

Featureset:

- [x] **OP#1 - ID Validation**: Verify identifier syntax using regex patterns
- [x] **OP#2 - ID Extraction**: Fetch identifiers from JSON objects or JSON Schema documents
- [x] **OP#3 - ID Parsing**: Decompose identifiers into constituent parts (vendor, package, namespace, type, version, etc.)
- [x] **OP#4 - ID Pattern Matching**: Match identifiers against patterns containing wildcards
- [x] **OP#5 - ID to UUID Mapping**: Generate deterministic UUIDs from GTS identifiers
- [x] **OP#6 - Instance Validation**: Validate object instances against their corresponding schemas
- [x] **OP#7 - Relationship Resolution**: Load all schemas and instances, resolve inter-dependencies, and detect broken references
- [x] **OP#8 - Compatibility Checking**: Verify that schemas with different MINOR versions are compatible
- [x] **OP#8.1 - Backward compatibility checking**
- [x] **OP#8.2 - Forward compatibility checking**
- [x] **OP#8.3 - Full compatibility checking**
- [x] **OP#9 - Version Casting**: Transform instances between compatible MINOR versions
- [x] **OP#10 - Query Execution**: Filter identifier collections using the GTS query language
- [x] **OP#11 - Attribute Access**: Retrieve property values and metadata using the attribute selector (`@`)
- [x] **OP#12 - Schema Validation**: Validate schema against its precedent schema
- [x] **OP#13 - Schema Traits Validation**: Validate `x-gts-traits-schema` / `x-gts-traits` across the inheritance chain

Other GTS spec [Reference Implementation](https://github.com/GlobalTypeSystem/gts-spec/blob/main/README.md#9-reference-implementation-recommendations) recommended features support:

- [x] **In-memory entities registry** - thread-safe GTS entities registry with conditional (conflict-aware) and staged, all-or-nothing batch registration
- [x] **CLI** - command-line interface for all GTS operations
- [x] **Web server** - a non-production web-server with REST API for the operations processing and testing
- [x] **GTS reference keywords** - `x-gts-type-ref`, `x-gts-instance-ref` and `x-gts-pattern-ref` annotations in schemas, with `none` / `any-present` / `any-valid` reference validation modes
- [x] **GTS schema modifiers** - `x-gts-abstract` and `x-gts-final`
- [x] **JSON Schema dialects** - Draft-07, Draft 2019-09 and Draft 2020-12
- [ ] **YAML support** - to support YAML files (*.yml, *.yaml) as input files
- [ ] **TypeSpec support** - add [typespec.io](https://typespec.io/) files (*.tsp) support
- [x] **UUID for instances** - to support UUID as ID in JSON instances
- [ ] **NuGet packages** - publish the libraries to nuget.org

## Regular expressions

`pattern`, `patternProperties`, `propertyNames` and `format: "regex"` (including trait, casting and OP#12 checks) follow the GTS [safe regular-expression profile](https://github.com/GlobalTypeSystem/gts-spec/blob/main/README.md#1101-regular-expression-execution-safety) (spec §11.0.1, ADR-0006). Expressions are first checked against the profile by a dedicated parser (a port of the Rust reference), then executed with .NET `System.Text.RegularExpressions` using `RegexOptions.NonBacktracking`, which guarantees linear-time matching.

## Installation

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer (see [`global.json`](global.json)).

The libraries are not yet published to NuGet. Until they are, build from source and reference the projects directly:

```bash
git clone https://github.com/GlobalTypeSystem/gts-dotnet.git
cd gts-dotnet
make build        # builds the solution and publishes the CLI to ./bin/gts
```

```xml
<!-- In your .csproj -->
<ItemGroup>
  <!-- identifiers only: parsing, validation, matching, UUIDs, extraction -->
  <ProjectReference Include="../gts-dotnet/Gts/Gts.csproj" />
  <!-- registry, validation, compatibility, casting, query, attribute access -->
  <ProjectReference Include="../gts-dotnet/Gts.Store/Gts.Store.csproj" />
</ItemGroup>
```

## Public API

The solution is split into projects; only the first two are intended as library dependencies:

| Project | Namespace | Contents |
|---|---|---|
| [`Gts`](Gts) | `Gts`, `Gts.Extraction` | Identifier grammar: `GtsId` (parse, validate, match, UUID), `GtsPattern` (matching and coverage), and `GtsJsonEntity` (ID and reference extraction from JSON). Only depends on [Pidgin](https://www.nuget.org/packages/Pidgin). |
| [`Gts.Store`](Gts.Store) | `Gts.Store` | `GtsRegistry` (in-memory store), instance/schema validation, compatibility checking, casting, queries and attribute access. Uses [JsonSchema.Net](https://www.nuget.org/packages/JsonSchema.Net). |
| [`Gts.Application`](Gts.Application) | `Gts.Application` | Host glue shared by the CLI and server: REST endpoint mapping (`MapGtsApi`), file loading, OpenAPI spec. |
| [`Gts.Cli`](Gts.Cli) | — | The `gts` command-line tool (also hosts the HTTP server). |
| [`Gts.Server`](Gts.Server) | — | Standalone ASP.NET Core host for the REST API. |

All registry operations are asynchronous (`ValueTask`), accept an optional `CancellationToken`, and report failures through result objects (`Ok` + a typed `GtsValidationFailure` reason) rather than exceptions.

## Usage

### Library

```csharp
using System.Text.Json.Nodes;
using Gts;              // GtsId, GtsPattern
using Gts.Extraction;   // GtsJsonEntity, GtsExtractOptions
using Gts.Store;        // GtsRegistry and result types
```

#### OP#1 - ID Validation

`GtsId.TryParse` returns a `ParseResult` that converts implicitly to `bool`; `GtsPattern.IsValid` also accepts wildcard patterns.

```csharp
if (GtsId.TryParse("gts.x.core.events.type.v1~", out var id))
    Console.WriteLine(id!.IsType);                    // True

bool ok = GtsId.TryParse("gts.x.core.events.type", out _);   // false: no version
GtsPattern.IsValid("gts.x.core.events.*");                    // true
```

#### OP#2 - ID Extraction

Extract entity and schema IDs from JSON objects and JSON Schema documents (`System.Text.Json`). The ID is taken from the first matching field in `GtsExtractOptions.EntityIdPropertyNames` (`gtsId`, `id`, `$id`, …) and the schema ID from the ID chain or from `GtsExtractOptions.SchemaIdPropertyNames` (`gtsTid`, `type`, …).

- **`GtsJsonEntity.ExtractId(JsonObject | JsonNode | JsonElement, GtsExtractOptions?)`** — returns `ExtractResult` (`Id`, `SchemaId`, `SelectedEntityField`, `SelectedSchemaIdField`, `IsSchema`).
- **`GtsJsonEntity.ExtractEntity(JsonObject, GtsExtractOptions?)`** — returns a `GtsJsonEntity` (parsed `GtsId`, `SchemaId`, `Content`, and every GTS ID in the tree as `GtsRefs`), ready to be saved in a registry.
- **`GtsJsonEntity.ExtractReferences(JsonNode?)`** — every GTS ID in the tree with its JSON path.

```csharp
var json = JsonNode.Parse("""
    { "id": "gts.x.core.events.type.v1~x.commerce.orders.order_placed.v1.0", "name": "Order placed" }
    """)!.AsObject();

var result = GtsJsonEntity.ExtractId(json);
// result.Id       == "gts.x.core.events.type.v1~x.commerce.orders.order_placed.v1.0"
// result.SchemaId == "gts.x.core.events.type.v1~"   (derived from the ID chain)

var entity = GtsJsonEntity.ExtractEntity(json);
// entity.GtsId, entity.SchemaId, entity.IsSchema, entity.GtsRefs
```

#### OP#3 - ID Parsing

`GtsId.Parse` throws `Gts.Parsing.ParseException` on invalid input; `TryParse` does not. `ParsePattern` / `TryParsePattern` accept a trailing wildcard.

```csharp
var id = GtsId.Parse("gts.x.core.events.type.v1~x.commerce.orders.order_placed.v1.0~");
foreach (var s in id.Segments)
    Console.WriteLine($"{s.Vendor}.{s.Package}.{s.Namespace}.{s.Type} major={s.VersionMajor} minor={s.VersionMinor}");
// x.core.events.type major=1 minor=
// x.commerce.orders.order_placed major=1 minor=0

var pattern = GtsId.ParsePattern("gts.x.core.events.*");
```

#### OP#4 - ID Pattern Matching

`GtsId.Matches` checks a concrete ID against a pattern. `GtsPattern` implements the full §10 semantics, including pattern coverage (`Covers`: every ID selected by the other pattern is also selected by this one).

```csharp
var candidate = GtsId.Parse("gts.x.core.events.type.v1~x.commerce.orders.order_placed.v1.0~");
candidate.Matches("gts.x.core.events.type.v1~*");   // true
candidate.Matches("gts.x.core.events.*");           // true
candidate.Matches("gts.y.*");                       // false

GtsPattern.TryParse("gts.x.core.events.type.v1~*", out var all);
GtsPattern.TryParse("gts.x.core.events.type.v1~x.commerce.*", out var commerce);
all!.Matches(candidate);        // true
all.Covers(commerce!);          // true
commerce!.Covers(all);          // false
```

#### OP#5 - ID to UUID Mapping

`ToGuid()` returns a deterministic UUID v5 in the GTS namespace (an instance ID ending in a UUID returns that UUID).

```csharp
Guid uuid = GtsId.Parse("gts.x.core.events.type.v1~").ToGuid();
```

#### Using the GTS registry

`GtsRegistry` stores schemas and instances and runs OP#6–OP#13 over them. Entities are registered as `GtsJsonEntity` values produced by `ExtractEntity`.

```csharp
var registry = GtsRegistry.InMemory(new GtsRegistryConfig(ValidateGtsReferences: false));

await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
    {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "$id": "gts://gts.x.shop.orders.order.v1.0~",
      "type": "object",
      "required": ["id", "amount"],
      "properties": {
        "id": { "type": "string" },
        "amount": { "type": "number", "minimum": 0 }
      }
    }
    """)!.AsObject()));

await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
    { "id": "gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1", "amount": 42 }
    """)!.AsObject()));
```

- **`SaveAsync`** stores or overwrites an entity. **`TrySaveAsync`** is an atomic compare-and-swap returning `GtsSaveOutcome.Added`, `Unchanged`, or `Conflict` (same ID, different content).
- **`GetAsync(GtsId)`**, **`GetByInstanceIdAsync(string)`** (GTS ID or opaque ID such as a UUID), **`GetAllAsync`**, **`CountAsync`**.
- **`StageAsync`** / **`CommitStagedAsync`** / **`CommitStagedBatchAsync`** / **`DiscardStagedAsync`** register entities that stay invisible until validated, and publish a batch all-or-nothing.

`GtsRegistryConfig.ValidateGtsReferences` is reserved and not currently consulted; references are checked by the `Validate*` operations below.

#### OP#6 - Instance Validation

Resolves the instance's type (the chained ID, or the `type` field for anonymous instances), loads the schema and its `gts://` `$ref` dependencies from the registry, and evaluates the instance, including GTS reference keywords.

```csharp
var r = await registry.ValidateInstanceAsync("gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1");
// r.Ok, r.FailureReason (e.g. GtsValidationFailure.SchemaValidationFailed), r.SchemaErrors

// Validate an unregistered JSON document against a registered schema,
// relaxing GTS reference checks to syntax only
var j = await registry.ValidateJsonAsync(
    JsonNode.Parse("""{ "id": "order-1", "amount": 1 }""")!.AsObject(),
    GtsId.Parse("gts.x.shop.orders.order.v1.0~"),
    refValidationMode: GtsRefValidationMode.None);
```

`GtsRefValidationMode` controls how `x-gts-*-ref` values are checked: `None` (syntax only), `AnyPresent` (the referenced entity must be registered), or `AnyValid` (default; it must also be valid). Use `GtsValidationFailures.ToWire()` to get the string form of a failure reason.

#### OP#7 - Relationship Resolution

```csharp
var rel = await registry.ResolveRelationshipsAsync();
// rel.IsConsistent, rel.EntityCount, rel.SchemaCount, rel.InstanceCount
foreach (var b in rel.BrokenReferences)
    Console.WriteLine($"{b.SourceId} -> {b.ReferencedId} at {b.SourcePath}: {b.Reason}");
```

#### OP#8 - Compatibility Checking

```csharp
var cmp = await registry.CompareMinorVersionSchemasAsync(
    GtsId.Parse("gts.x.shop.orders.order.v1.0~"),
    GtsId.Parse("gts.x.shop.orders.order.v1.1~"));
// cmp.IsBackwardEvolutionCompatible / BackwardEvolutionErrors
// cmp.IsForwardEvolutionCompatible  / ForwardEvolutionErrors
// cmp.IsEvolutionFullyCompatible

var report = await registry.CheckMinorVersionCompatibilityAsync();   // every minor pair in the registry
// report.AreAllCompatible, report.IncompatiblePairs
```

#### OP#9 - Version Casting

Casts a stored instance to another minor version of its type: fills defaults, updates GTS ID `const` fields, prunes properties when `additionalProperties` is `false`, then validates against the target schema.

```csharp
var cast = await registry.CastInstanceAsync(
    "gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1",
    GtsId.Parse("gts.x.shop.orders.order.v1.1~"));
// cast.Ok, cast.CastedContent, cast.Comparison, cast.FailureReason, cast.SchemaValidationErrors
```

#### OP#10 - Query Execution

ID patterns with optional attribute filters (combined with AND). `limit` accepts 1–1000 (default 100).

```csharp
var q = await registry.QueryAsync("gts.x.shop.orders.order.v1.0~x.shop.*[amount=42]", limit: 10);
// q.Ok, q.Error, q.Results (matching entity bodies)
```

#### OP#11 - Attribute Access

```csharp
var attr = await registry.GetAttributeAsync("gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1@amount");
// attr.Resolved, attr.Value (JsonNode), attr.Error, attr.AvailableFields
```

#### OP#12 / OP#13 - Schema and Traits Validation

Validates a schema's dialect, GTS keywords, `$ref` format and regular expressions, checks that it is compatible with every precedent type in its ID chain, and validates `x-gts-traits-schema` / `x-gts-traits` across the chain.

```csharp
var sv = await registry.ValidateSchemaAsync("gts.x.shop.orders.order.v1.0~x.shop.orders.priority_order.v1~");
// sv.Ok, sv.FailureReason (e.g. PrecedentIncompatible, TraitValidationFailed), sv.Errors

// Or validate an unregistered schema document against the registered precedents
JsonObject schemaDocument = /* a GTS Type Schema */;
var doc = await registry.ValidateSchemaAsync(
    GtsId.Parse("gts.x.shop.orders.order.v1.0~x.shop.orders.priority_order.v1~"), schemaDocument);
```

### CLI

Build the CLI with `make build`; the binary is written to `./bin/gts`. Global options go before the command, and every command prints JSON to stdout.

```bash
# Show help / version
gts help
gts version

# Basic operations (no file loading required)
gts validate-id -id 'gts.x.shop.orders.order.v1~'                                    # OP#1
gts parse-id -id 'gts.x.shop.orders.order.v1~'                                       # OP#3
gts match-id-pattern -pattern 'gts.x.shop.*' -candidate 'gts.x.shop.orders.order.v1~' # OP#4
gts uuid -id 'gts.x.shop.orders.order.v1~'                                           # OP#5

# Operations with loaded entities.
# Placeholders: point -path at your own schema/instance files or directories
# and substitute their IDs.
gts -path ./examples validate -id 'gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1'          # OP#6
gts -path ./examples relationships -id 'gts.x.shop.orders.order.v1.0~'                         # OP#7
gts -path ./examples compatibility -old 'gts.x.shop.orders.order.v1.0~' -new 'gts.x.shop.orders.order.v1.1~'  # OP#8
gts -path ./examples cast -from 'gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1' -to 'gts.x.shop.orders.order.v1.1~'  # OP#9
gts -path ./examples query -expr 'gts.x.shop.*' -limit 10                                      # OP#10
gts -path ./examples attr -path 'gts.x.shop.orders.order.v1.0~x.shop._.order_42.v1@amount'     # OP#11
gts -path ./examples list -limit 100

# Start the HTTP server / write the OpenAPI specification
gts -path ./examples server -host 127.0.0.1 -port 8000
gts openapi -out openapi.json
```

Global options:

| Option | Environment variable | Description |
|---|---|---|
| `-path <paths>` | `GTS_PATH` | Comma-separated files or directories to load (`.json`, `.jsonc`, `.gts`; a file may hold one entity or an array) |
| `-config <file>` | `GTS_CONFIG` | Optional JSON config with `entity_id_fields` / `schema_id_fields` arrays overriding the extraction field names |
| `-v`, `--verbose`, `-v2` | `GTS_VERBOSE` | Verbose logging |

### Web server

A non-production HTTP server exposes the operations over REST, using the API exercised by the gts-spec conformance suite. The in-memory registry starts empty unless `-path` is given.

```bash
gts server                                   # http://127.0.0.1:8000
gts server -host 0.0.0.0 -port 8001
gts -path ./examples server                  # pre-populate from files

# or the standalone host (listens on ASPNETCORE_URLS, default http://127.0.0.1:8000)
dotnet run --project Gts.Server
```

To embed the API in your own ASP.NET Core app, call `app.MapGtsApi(registry)` from `Gts.Application`. The full request/response schema is served at `GET /openapi`.

#### Entity management

| Method | Path | Description |
|---|---|---|
| `GET` | `/entities?limit=` | List registered entities |
| `GET` | `/entities/{id}` | Get an entity by GTS ID or opaque ID |
| `POST` | `/entities` | Register one entity; `?validate=true` validates before publishing, `?gts-ref-validation=none\|any-present\|any-valid` |
| `POST` | `/entities/bulk` | Register an array of entities |
| `POST` | `/type-schemas` | Register an array of type schemas; with `?validate=true` the batch is validated order-independently and published atomically |

#### GTS operations

| Method | Path | Operation |
|---|---|---|
| `GET` | `/validate-id?gts_id=` | OP#1 - ID Validation |
| `POST` | `/extract-id` | OP#2 - ID Extraction |
| `GET` | `/parse-id?gts_id=` | OP#3 - ID Parsing |
| `GET` | `/match-id-pattern?candidate=&pattern=` | OP#4 - ID Pattern Matching |
| `GET` | `/uuid?gts_id=` | OP#5 - ID to UUID Mapping |
| `POST` | `/validate-instance` | OP#6 - Validate a registered instance (`{"instance_id": "..."}`) |
| `POST` | `/validate-json`, `/validate-json/{type}` | OP#6 - Validate an unregistered JSON document |
| `GET` | `/resolve-relationships?gts_id=` | OP#7 - Relationship Resolution |
| `GET` | `/compatibility?old_type_id=&new_type_id=` | OP#8 - Compatibility Checking |
| `POST` | `/cast` | OP#9 - Version Casting |
| `GET` | `/query?expr=&limit=` | OP#10 - Query Execution |
| `GET` | `/attr?gts_with_path=` | OP#11 - Attribute Access |
| `POST` | `/validate-type-schema`, `/validate-schema` | OP#12/OP#13 - Schema and Traits Validation (`{"type_id": "..."}`) |
| `POST` | `/validate-entity` | Validate any registered entity, schema or instance (`{"entity_id": "..."}`) |
| `GET` | `/openapi` | OpenAPI specification |

## Development

Common tasks are wrapped in the [`Makefile`](Makefile); run `make help` to
list every target. Requires the .NET SDK (see [`global.json`](global.json)).

```bash
make build       # build the solution and publish the gts CLI to ./bin/gts
make dev-fmt     # apply formatting fixes
make fmt         # verify formatting (dotnet format whitespace + style)
make lint        # run analyzers (dotnet format analyzers)
make test        # run the unit tests
make security    # audit NuGet dependencies for known vulnerabilities
make coverage    # collect code coverage
make check       # full local gate: fmt + lint + test + gts-spec-tests
```

## Testing

`make gts-spec-tests` runs the shared
[gts-spec](https://github.com/GlobalTypeSystem/gts-spec) conformance suite
against a freshly built server. Tests come from the published runner
image `ghcr.io/globaltypesystem/gts-spec-tests`; the tag is pinned in
[`.gts-spec-version`](.gts-spec-version) as an immutable
`vMAJOR.MINOR.PATCH` — every commit reproduces the same test run, and
rolling forward is a deliberate bump of that file. Requires a working
Docker daemon plus the .NET SDK (the target builds the CLI binary before
pulling the test-runner image).

```bash
make gts-spec-tests                                    # full suite on :8000
make gts-spec-tests PORT=8001                          # different port
make gts-spec-tests TEST=test_op1_id_validation.py     # single file / selector
```

Opt into the rolling minor tag, try a different patch, or test a fork:

```bash
make gts-spec-tests GTS_SPEC_VERSION=v0.17             # rolling vMAJOR.MINOR
make gts-spec-tests GTS_SPEC_VERSION=v0.17.0           # specific patch
make gts-spec-tests GTS_SPEC_IMAGE=ghcr.io/your-fork/gts-spec-tests
```

Iterating on the test suite itself? Mount a local checkout over `/tests`:

```bash
make gts-spec-tests GTS_SPEC_TESTS_DIR=../gts-spec/tests
```

For tight test-edit loops, keep a long-running server in one terminal and
re-run targeted tests in another:

```bash
# Terminal 1
make gts-server PORT=8001

# Terminal 2
make gts-spec-tests-run PORT=8001 TEST=test_op6_schema_validation.py
```

## License

Apache License 2.0
