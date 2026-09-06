using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Validation;

public class RelationshipResolutionTests
{
    [Fact]
    public async Task Fully_linked_schemas_and_instance_are_consistent()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.relres.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object"
            }
            """;

        const string derivedSchema = """
            {
              "$$id": "gts://gts.x.relres.events.type.v1~x.relres.orders.order_placed.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.relres.events.type.v1~" }
              ]
            }
            """;

        const string instance = """
            {
              "type": "gts.x.relres.events.type.v1~x.relres.orders.order_placed.v1.0~",
              "id": "gts.x.relres.events.type.v1~x.relres.orders.order_placed.v1.0~x.relres._.evt.v1.0"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(derivedSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var result = await registry.ResolveRelationshipsAsync();

        Assert.True(result.IsConsistent);
        Assert.Equal(3, result.EntityCount);
        Assert.Equal(2, result.SchemaCount);
        Assert.Equal(1, result.InstanceCount);
        Assert.Empty(result.BrokenReferences);
    }

    [Fact]
    public async Task Missing_schema_target_from_ref_is_reported()
    {
        const string derivedOnly = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~x.test6.rel.missing_base.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.test6.events.type.v1~" }
              ]
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(derivedOnly)!.AsObject()));

        var result = await registry.ResolveRelationshipsAsync();

        Assert.False(result.IsConsistent);
        var br = Assert.Single(result.BrokenReferences);
        Assert.Equal("MissingSchema", br.Reason);
        Assert.Equal("gts.x.test6.events.type.v1~", br.ReferencedId);
        Assert.True(br.SourceIsSchema);
    }

    [Fact]
    public async Task Missing_instance_target_is_reported()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "peer": { "type": "string" }
              }
            }
            """;

        const string instance = """
            {
              "type": "gts.x.test6.events.type.v1~",
              "id": "gts.x.test6.events.type.v1~x.test6.rel.peer_self.v1.0",
              "peer": "gts.x.test6.events.type.v1~x.test6.rel.peer_other.v1.0"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var result = await registry.ResolveRelationshipsAsync();

        Assert.False(result.IsConsistent);
        var br = Assert.Single(result.BrokenReferences);
        Assert.Equal("MissingInstance", br.Reason);
        Assert.Equal("gts.x.test6.events.type.v1~x.test6.rel.peer_other.v1.0", br.ReferencedId);
        Assert.Contains("peer", br.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pattern_reference_does_not_require_stored_target()
    {
        const string schema = """
            {
              "$$id": "gts://gts.x.relres4.modules.capability.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "accepts": { "const": "gts.x.relres4.*" }
              }
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(schema)!.AsObject()));

        var result = await registry.ResolveRelationshipsAsync();

        Assert.True(result.IsConsistent);
    }

    [Fact]
    public async Task Chained_instance_without_type_field_reports_missing_derived_schema()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.relres5.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object"
            }
            """;

        const string instance = """
            {
              "id": "gts.x.relres5.events.type.v1~x.relres5.orders.child.v1.0~x.relres5._.i.v1.0"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        var result = await registry.ResolveRelationshipsAsync();

        Assert.False(result.IsConsistent);
        var br = Assert.Single(result.BrokenReferences);
        Assert.Equal("MissingTypeBinding", br.Reason);
        Assert.Equal("gts.x.relres5.events.type.v1~x.relres5.orders.child.v1.0~", br.ReferencedId);
        Assert.Equal("(type binding)", br.SourcePath);
    }

    [Fact]
    public async Task GetAllAsync_includes_anonymous_instances_in_count()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.relres6anon.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["id", "type"],
              "properties": {
                "type": { "type": "string" },
                "id": { "type": "string", "format": "uuid" }
              }
            }
            """;

        const string instance = """
            {
              "type": "gts.x.relres6anon.events.type.v1~",
              "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instance)!.AsObject()));

        Assert.Equal(2, await registry.CountAsync());
        var all = await registry.GetAllAsync();
        Assert.Equal(2, all.Count);
    }
}
