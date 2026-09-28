using System.Text.Json.Nodes;
using Gts.Store.Validation;

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