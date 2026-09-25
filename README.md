> Status: initial draft v0.1, not for production use

# GTS .NET Library

An idiomatic C#/.NET library for working with **GTS** ([Global Type System](https://github.com/gts-spec/gts-spec)) identifiers and JSON/JSON Schema artifacts.

Supported GTS spec version: `v0.14.1` (pinned in [`.gts-spec-version`](.gts-spec-version))

## Roadmap

Featureset:

- [x] **OP#1 - ID Validation**: Verify identifier syntax using regex patterns
- [x] **OP#2 - ID Extraction**: Fetch identifiers from JSON objects or JSON Schema documents
- [x] **OP#3 - ID Parsing**: Decompose identifiers into constituent parts (vendor, package, namespace, type, version, etc.)
- [x] **OP#4 - ID Pattern Matching**: Match identifiers against patterns containing wildcards
- [x] **OP#5 - ID to UUID Mapping**: Generate deterministic UUIDs from GTS identifiers
- [x] **OP#6 - Instance Validation**: Validate object instances against their corresponding schemas
- [ ] **OP#7 - Relationship Resolution**: Load all schemas and instances, resolve inter-dependencies, and detect broken references
- [ ] **OP#8 - Compatibility Checking**: Verify that schemas with different MINOR versions are compatible
- [ ] **OP#8.1 - Backward compatibility checking**
- [ ] **OP#8.2 - Forward compatibility checking**
- [ ] **OP#8.3 - Full compatibility checking**
- [ ] **OP#9 - Version Casting**: Transform instances between compatible MINOR versions
- [ ] **OP#10 - Query Execution**: Filter identifier collections using the GTS query language
- [ ] **OP#11 - Attribute Access**: Retrieve property values and metadata using the attribute selector (`@`)
- [ ] **OP#12 - Schema Validation**: Validate schema against its precedent schema

## Installation

```bash
#TODO: NuGet packages
```

## Usage

### Library

```csharp
using Gts;
using Gts.Extraction;
```

### OP#2 - ID Extraction

Extract entity and schema IDs from JSON objects (and JSON Schema documents). Uses `System.Text.Json` (`JsonObject` / `JsonNode`).

- **ExtractId(JsonObject, GtsExtractOptions?)** — returns `ExtractResult` with `Id`, `SchemaId`, which fields were used, and `IsSchema`. `Id` is null when no valid ID is found.
- **ExtractId(JsonNode?)** / **ExtractId(JsonElement)** — overloads for different JSON sources.
- **ExtractEntity(JsonObject, …)** — returns `GtsJsonEntity` with parsed `GtsId`, schema ID, and all **GtsRefs** (every GTS ID in the tree with path).
- **ExtractReferences(JsonNode?)** — walks the tree and returns all GTS IDs with their JSON paths.
- **GtsExtractOptions.Default** — default entity fields.

```csharp
var node = JsonNode.Parse("""{ "gtsId": "gts.acme.order.ns.invoice.v1.0", "name": "Order 1" }""");

var result = GtsExtract.ExtractId(node.AsObject());
// result.Id, result.SchemaId, result.SelectedEntityField, result.IsSchema

var entity = GtsExtract.ExtractEntity(node.AsObject());
// entity.GtsId, entity.GtsRefs (all GTS IDs + paths)

var refs = GtsExtract.ExtractReferences(node);
```

### OP#3 - ID Parsing

Decompose GTS identifiers into constituent parts (vendor, package, namespace, type, version, etc.).

- **Parse** a type ID (trailing `~`) or instance ID; **TryParse** for safe parsing without exceptions.
- **ParsePattern** / **TryParsePattern** for patterns that may end with a wildcard (`.*`).

```csharp
// Parsing
var id = GtsId.Parse("gts.acme.order.ns.invoice.v1~");

// Safe parsing
if (GtsId.TryParse("gts.vendor.pkg.ns.type.v1.0", out var id))
    // do something

// Pattern (for matching)
var pattern = GtsId.ParsePattern("gts.acme.order.*");

// Safe pattern parsing
if (GtsId.TryParsePattern("gts.acme.order.*", out var pattern))
	// do something
```

### OP#4 - ID Pattern Matching

Match identifiers against patterns containing wildcards.

- **Matches(GtsId pattern)** — match this ID against a parsed pattern.
- **Matches(string pattern)** — match this ID against a pattern string.

```csharp
var candidate = GtsId.Parse("gts.acme.order.ns.invoice.v1.0");

// Match against pattern (parsed)
var pattern = GtsId.ParsePattern("gts.acme.order.*");
candidate.Matches(pattern);  // true

// Match against pattern string
candidate.Matches("gts.acme.order.*");       // true
candidate.Matches("gts.acme.order.ns.*");    // true
candidate.Matches("gts.other.*");            // false

// Exact match (no wildcard)
candidate.Matches("gts.acme.order.ns.invoice.v1.0");  // true
```

### OP#5 - ID to UUID Mapping

Generate a deterministic UUID v5 from a GTS identifier. The same ID always yields the same UUID (RFC 4122, namespace + name hashed).

- **ToGuid()** — returns a `Guid` for this GTS ID using the standard GTS namespace.

```csharp
var id = GtsId.Parse("gts.acme.order.ns.invoice.v1.0");
Guid uuid = id.ToGuid();  // deterministic: same ID → same UUID every time
```

### OP#6 - Instance Validation

Validate stored JSON instances against Draft 07 JSON Schemas registered in the same `GtsRegistry`, including `gts://` `$ref` between schemas and GTS `$$id` / `$$ref` / `$$schema` keywords on schema documents.

- **ValidateInstanceAsync(instanceId)** — loads the instance (by GTS instance id or opaque id such as a UUID), resolves its type (chained instance id or `type` field), fetches the schema from the store, and evaluates the instance with [JsonSchema.Net](https://www.nuget.org/packages/JsonSchema.Net). Returns `GtsInstanceValidationResult` with `Ok`, `Id`, `FailureReason`, and optional `SchemaErrors`.

```csharp
using Gts.Extraction;
using Gts.Store;

var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
// Save schemas (JSON Schema with $id or $$id) and instances via SaveAsync(GtsJsonEntity.ExtractEntity(...))
var result = await registry.ValidateInstanceAsync("gts.vendor.pkg.ns.type.v1~x._.myinst.v1");
// result.Ok, result.FailureReason, result.SchemaErrors
```

## Development

Common tasks are wrapped in the [`Makefile`](Makefile); run `make help` to
list every target. Requires the .NET SDK (see [`global.json`](global.json)).

```bash
make build       # build the solution and publish the gts CLI to ./bin/gts
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
make gts-spec-tests GTS_SPEC_VERSION=v0.11             # rolling vMAJOR.MINOR
make gts-spec-tests GTS_SPEC_VERSION=v0.11.0           # specific patch
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
make gts-spec-tests-run PORT=8001 TEST=test_op6_instance_validation.py
```

## License

Apache License 2.0

