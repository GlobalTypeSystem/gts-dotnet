using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gts.Store.Validation;
using Json.Schema;

namespace Gts.Tests.Validation;

public class JsonInfrastructureTests
{
    [Fact]
    public void Json_pointer_resolves_escaped_tokens_and_array_indices()
    {
        var root = JsonNode.Parse("""{"a/b":{"~key":["zero","one"]}}""");

        var found = GtsJsonPointer.TryEvaluate(root, "#/a~1b/~0key/1", out var value);

        Assert.True(found);
        Assert.Equal("one", value!.GetValue<string>());
    }

    [Fact]
    public void Schema_engine_uses_strict_registered_uuid_format()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "string",
              "format": "uuid"
            }
            """)!.AsObject();

        var valid = GtsJsonSchemaEngine.Default.EvaluateInline(JsonValue.Create("11111111-2222-3333-8444-555555555555"), schema);
        var invalid = GtsJsonSchemaEngine.Default.EvaluateInline(JsonValue.Create("{11111111-2222-3333-8444-555555555555}"), schema);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void Schema_engine_ignores_pattern_keys_in_annotations()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "examples": [{ "pattern": "[" }]
            }
            """)!.AsObject();

        var result = GtsJsonSchemaEngine.Default.EvaluateInline(new JsonObject(), schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Schema_engine_only_matches_patterns_at_their_instance_location()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "code": { "type": "string", "pattern": "(a+)+$" }
              },
              "additionalProperties": true
            }
            """)!.AsObject();
        var instance = new JsonObject
        {
            ["code"] = "a",
            ["description"] = new string('a', 40_000) + "!"
        };

        var result = GtsJsonSchemaEngine.Default.EvaluateInline(instance, schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Schema_engine_bounds_patterns_in_nested_resources()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "code": {
                  "$schema": "http://json-schema.org/draft-07/schema#",
                  "type": "string",
                  "pattern": "(a+)+$"
                }
              }
            }
            """)!.AsObject();
        var instance = new JsonObject { ["code"] = new string('a', 40_000) + "!" };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<RegexMatchTimeoutException>(() => GtsJsonSchemaEngine.Default.EvaluateInline(instance, schema));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Schema_engine_validation_builds_standard_schema_locations()
    {
        var schema = JsonNode.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "$id": "gts://gts.x.regex._.invalid.v1~",
              "definitions": {
                "invalid": { "type": "string", "pattern": "[" }
              },
              "examples": [{ "pattern": "[" }]
            }
            """)!.AsObject();

        Assert.Throws<JsonSchemaException>(() => GtsJsonSchemaEngine.Default.ValidateSchema(
            GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(schema)));
    }

    [Fact]
    public void Schema_engine_rejects_a_missing_dialect()
    {
        var schema = JsonNode.Parse("""{ "type": "object" }""")!.AsObject();

        Assert.Throws<JsonSchemaException>(() => GtsJsonSchemaEngine.Default.EvaluateInline(new JsonObject(), schema));
    }

    [Theory]
    [InlineData("http://json-schema.org/draft-06/schema#")]
    [InlineData("https://json-schema.org/draft/2025-01/schema")]
    [InlineData("https://json-schema.org/draft/2020-21/schema")]
    [InlineData("https://example.invalid/schema")]
    public void Schema_engine_rejects_an_unsupported_dialect(string dialect)
    {
        var schema = new JsonObject { ["$schema"] = dialect, ["type"] = "object" };

        Assert.Throws<JsonSchemaException>(() => GtsJsonSchemaEngine.Default.EvaluateInline(new JsonObject(), schema));
    }

    [Theory]
    [InlineData("gts.x.pkg.ns.type.v1~")]
    [InlineData("gts.x.pkg.ns.type.v1~123e4567-e89b-42d3-a456-426614174000")]
    public void Shared_id_parser_accepts_supported_identifier_shapes(string id)
    {
        Assert.True(GtsId.TryParse(id, out _));
    }

    [Theory]
    [InlineData("gts.x.pkg.ns.type.v-1~")]
    [InlineData("gts.x.pkg.ns.type.v1.-1~")]
    public void Shared_id_parser_rejects_negative_versions(string id)
    {
        Assert.False(GtsId.TryParse(id, out _));
    }
}