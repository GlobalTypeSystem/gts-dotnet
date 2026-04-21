using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Validation;

public class InstanceCastTests
{
    [Fact]
    public async Task CastInstanceAsync_upgrade_minor_when_evolution_allows()
    {
        const string s0 = """
            {
              "$$id": "gts://gts.x.cast.demo.item.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["a"],
              "properties": {
                "a": { "type": "string" }
              }
            }
            """;

        const string s1 = """
            {
              "$$id": "gts://gts.x.cast.demo.item.v1.1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["a"],
              "properties": {
                "a": { "type": "string" },
                "b": { "type": "string", "default": "default-b" }
              }
            }
            """;

        const string instance = """
            {
              "gtsId": "gts.x.cast.demo.item.v1.0~x.cast.ns.myinst.v1.0",
              "a": "hello"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s0)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s1)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var to = GtsId.Parse("gts.x.cast.demo.item.v1.1~");
        var result = await registry.CastInstanceAsync(
            "gts.x.cast.demo.item.v1.0~x.cast.ns.myinst.v1.0",
            to);

        Assert.True(result.Ok);
        Assert.NotNull(result.CastedContent);
        Assert.Equal("default-b", result.CastedContent!["b"]!.GetValue<string>());
        Assert.True(result.Comparison!.IsBackwardEvolutionCompatible);
    }

    [Fact]
    public async Task CastInstanceAsync_fails_when_backward_evolution_blocks_upgrade()
    {
        const string s0 = """
            {
              "$$id": "gts://gts.x.cast.demo.doc.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object"
            }
            """;

        const string s1 = """
            {
              "$$id": "gts://gts.x.cast.demo.doc.v1.1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["x"]
            }
            """;

        const string instance = """
            {
              "gtsId": "gts.x.cast.demo.doc.v1.0~x.cast.ns.empty.v1.0"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s0)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s1)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var result = await registry.CastInstanceAsync(
            "gts.x.cast.demo.doc.v1.0~x.cast.ns.empty.v1.0",
            GtsId.Parse("gts.x.cast.demo.doc.v1.1~"));

        Assert.False(result.Ok);
        Assert.Equal("IncompatibleMinorEvolution", result.FailureReason);
        Assert.NotNull(result.Comparison);
        Assert.False(result.Comparison!.IsBackwardEvolutionCompatible);
    }

    [Fact]
    public async Task CastInstanceAsync_fails_when_target_is_not_minor_variant()
    {
        const string sWidget = """
            {
              "$$id": "gts://gts.x.cast.demo.widget.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["k"],
              "properties": { "k": { "type": "string" } }
            }
            """;

        const string sOther = """
            {
              "$$id": "gts://gts.x.cast.other.thing.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["k"],
              "properties": { "k": { "type": "string" } }
            }
            """;

        const string instance = """
            {
              "gtsId": "gts.x.cast.demo.widget.v1.0~x.cast.ns.i.v1.0",
              "k": "v"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(sWidget)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(sOther)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var result = await registry.CastInstanceAsync(
            "gts.x.cast.demo.widget.v1.0~x.cast.ns.i.v1.0",
            GtsId.Parse("gts.x.cast.other.thing.v1.0~"));

        Assert.False(result.Ok);
        Assert.Equal("NotMinorVariantPair", result.FailureReason);
    }
}
