using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;
using Gts.Store.Validation;

namespace Gts.Tests.Validation;

public class ReferenceResolutionHardeningTests
{
    private const string Draft07 = "http://json-schema.org/draft-07/schema#";
    private const string TargetId = "gts.x.test.hardening.target.v1~";

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject ResolveThrough(JsonObject target) =>
        GtsSchemaDependencyGraph.Resolve(
            Parse($$"""{ "$ref": "gts://{{TargetId}}" }"""),
            id => id.Id == TargetId ? target : null);

    [Fact]
    public void Resolve_rejects_exponential_reference_fanout()
    {
        var defs = new JsonObject();
        const int levels = 30;
        for (var i = 0; i < levels; i++)
            defs[$"d{i}"] = Parse($$"""{ "allOf": [{ "$ref": "#/$defs/d{{i + 1}}" }, { "$ref": "#/$defs/d{{i + 1}}" }] }""");
        defs[$"d{levels}"] = Parse("""{ "type": "string" }""");
        var target = new JsonObject { ["$defs"] = defs, ["$ref"] = "#/$defs/d0" };

        Assert.Throws<GtsSchemaTooLargeException>(() => ResolveThrough(target));
    }

    [Fact]
    public void Resolve_keeps_literal_data_and_resolves_schema_map_members()
    {
        var target = Parse("""
            {
              "$defs": { "x": { "type": "string" } },
              "properties": {
                "literal": { "const": { "$ref": "#/$defs/x" } },
                "default": { "$ref": "#/$defs/x" }
              }
            }
            """);

        var resolved = ResolveThrough(target);

        var properties = resolved["properties"]!.AsObject();
        Assert.Equal("#/$defs/x", properties["literal"]!["const"]!["$ref"]!.GetValue<string>());
        Assert.Equal("string", properties["default"]!["type"]!.GetValue<string>());
    }

    private static IReadOnlyList<string> ValidateInstance(string instance, string schema, string selected = "gts.x.test.hardening.leaf.v1~") =>
        GtsRefValidator.ValidateInstance(Parse(instance), Parse(schema), selected, Array.Empty<GtsJsonEntity>(), GtsRefValidationMode.None);

    [Fact]
    public void Not_with_reference_keyword_admits_values_of_another_type()
    {
        var schema = $$"""
            {
              "$schema": "{{Draft07}}",
              "properties": { "scope": { "not": { "type": "string", "x-gts-pattern-ref": "{{TargetId}}" } } }
            }
            """;

        Assert.Empty(ValidateInstance("""{ "scope": 5 }""", schema));
        Assert.NotEmpty(ValidateInstance($$"""{ "scope": "{{TargetId}}" }""", schema));
    }

    [Fact]
    public void Recursive_local_reference_checks_every_level()
    {
        var schema = $$"""
            {
              "$schema": "{{Draft07}}",
              "definitions": {
                "node": {
                  "properties": {
                    "id": { "type": "string", "x-gts-type-ref": "{{TargetId}}" },
                    "child": { "$ref": "#/definitions/node" }
                  }
                }
              },
              "$ref": "#/definitions/node"
            }
            """;

        Assert.Empty(ValidateInstance($$"""{ "child": { "child": { "id": "{{TargetId}}" } } }""", schema));
        Assert.NotEmpty(ValidateInstance("""{ "child": { "child": { "id": "gts.x.test.hardening.other.v1~" } } }""", schema));
    }

    private const string InstanceId = "gts.x.test.hardening.target.v1~x.test.hardening.item.v1";

    // Both sides of instance validation: the JSON Schema engine and the reference walk.
    private static bool Accepts(string instance, string schema) =>
        GtsJsonSchemaEvaluator.EvaluateInline(Parse(instance), GtsSchemaDocumentNormalizer.ForJsonSchemaEvaluation(Parse(schema))).IsValid
        && ValidateInstance(instance, schema).Count == 0;

    [Fact]
    public void Inactive_conditional_branch_does_not_check_reference_candidates()
    {
        var schema = $$"""
            {
              "$schema": "{{Draft07}}",
              "if": { "properties": { "kind": { "const": "type" } } },
              "then": { "properties": { "ref": { "x-gts-type-ref": "{{TargetId}}" } } },
              "else": { "properties": { "ref": { "x-gts-instance-ref": "{{TargetId}}*" } } }
            }
            """;

        Assert.True(Accepts($$"""{ "kind": "instance", "ref": "{{InstanceId}}" }""", schema));
        Assert.True(Accepts($$"""{ "kind": "type", "ref": "{{TargetId}}" }""", schema));
        Assert.False(Accepts($$"""{ "kind": "type", "ref": "{{InstanceId}}" }""", schema));
    }

    [Theory]
    [InlineData("""{ "required": ["extra"] }""")]
    [InlineData("false")]
    public void Reference_condition_selects_the_branch_that_applies(string thenSchema)
    {
        var schema = $$"""
            {
              "$schema": "{{Draft07}}",
              "if": { "properties": { "ref": { "x-gts-type-ref": "{{TargetId}}" } } },
              "then": {{thenSchema}}
            }
            """;

        Assert.False(Accepts($$"""{ "ref": "{{TargetId}}" }""", schema));
        Assert.True(Accepts("""{ "ref": "gts.x.test.hardening.other.v1~" }""", schema));
    }

    [Fact]
    public void Self_reference_accepts_unsigned_32_bit_versions()
    {
        var schema = Parse("""{ "properties": { "scope": { "type": "string", "x-gts-pattern-ref": "/$id" } } }""");

        Assert.Empty(GtsRefValidator.ValidatePatterns(schema, "gts.x.test.hardening.leaf.v4294967295~"));
    }
}
