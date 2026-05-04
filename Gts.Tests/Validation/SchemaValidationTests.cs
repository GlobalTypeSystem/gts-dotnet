using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Validation;

public class SchemaValidationTests
{
    // Base has no "required" so FlattenSchema (which does not inline $$ref) matches derivation checks with allOf + $$ref.
    private const string BaseSchema = """
        {
          "$$id": "gts://gts.x.schema12.events.type.v1~",
          "$$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {
            "type": { "type": "string" },
            "id": { "type": "string" },
            "tenantId": { "type": "string", "format": "uuid" },
            "payload": { "type": "object" }
          },
          "additionalProperties": false
        }
        """;

    private const string DerivedSchemaValid = """
        {
          "$$id": "gts://gts.x.schema12.events.type.v1~x.commerce.orders.order_placed.v1.0~",
          "$$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "allOf": [
            { "$$ref": "gts://gts.x.schema12.events.type.v1~" },
            {
              "type": "object",
              "required": ["type", "payload"],
              "properties": {
                "type": { "const": "gts.x.schema12.events.type.v1~x.commerce.orders.order_placed.v1.0~" },
                "payload": {
                  "type": "object",
                  "required": ["orderId"],
                  "properties": {
                    "orderId": { "type": "string", "format": "uuid" }
                  }
                }
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task Base_schema_passes_without_precedent_chain()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(BaseSchema)!.AsObject()));

        var r = await registry.ValidateSchemaAsync("gts.x.schema12.events.type.v1~");

        Assert.True(r.Ok);
        Assert.Equal("gts.x.schema12.events.type.v1~", r.SchemaId);
        Assert.Null(r.FailureReason);
    }

    [Fact]
    public async Task Derived_schema_passes_when_precedent_stored()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(BaseSchema)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(DerivedSchemaValid)!.AsObject()));

        var r = await registry.ValidateSchemaAsync(
            GtsId.Parse("gts.x.schema12.events.type.v1~x.commerce.orders.order_placed.v1.0~"));

        Assert.True(r.Ok);
    }

    [Fact]
    public async Task Document_overload_passes_without_saving_derived()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(BaseSchema)!.AsObject()));

        var derived = JsonNode.Parse(DerivedSchemaValid)!.AsObject();
        var id = GtsId.Parse("gts.x.schema12.events.type.v1~x.commerce.orders.order_placed.v1.0~");
        var r = await registry.ValidateSchemaAsync(id, derived);

        Assert.True(r.Ok);
        Assert.Equal(1, await registry.CountAsync());
    }

    [Fact]
    public async Task Derived_fails_when_precedent_missing()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var derived = JsonNode.Parse(DerivedSchemaValid)!.AsObject();
        var id = GtsId.Parse("gts.x.schema12.events.type.v1~x.commerce.orders.order_placed.v1.0~");

        var r = await registry.ValidateSchemaAsync(id, derived);

        Assert.False(r.Ok);
        Assert.Equal("PrecedentIncompatible", r.FailureReason);
        Assert.Contains(r.Errors!, e => e.Contains("Precedent schema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_ref_format_fails()
    {
        const string badRef = """
            {
              "$$id": "gts://gts.x.schema12.badref.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "x": { "$ref": "https://example.com/other.json" }
              }
            }
            """;
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(badRef)!.AsObject()));

        var r = await registry.ValidateSchemaAsync("gts.x.schema12.badref.type.v1~");

        Assert.False(r.Ok);
        Assert.Equal("InvalidRefFormat", r.FailureReason);
        Assert.NotNull(r.Errors);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public async Task Non_type_id_returns_invalid()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var r = await registry.ValidateSchemaAsync("gts.x.schema12.events.type.v1.0");

        Assert.False(r.Ok);
        Assert.Equal("InvalidSchemaId", r.FailureReason);
    }

    [Fact]
    public async Task Missing_entity_returns_schema_not_found()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var r = await registry.ValidateSchemaAsync("gts.x.schema12.events.type.v1~");

        Assert.False(r.Ok);
        Assert.Equal("SchemaNotFound", r.FailureReason);
    }

    [Fact]
    public async Task Instance_id_returns_invalid_schema_id()
    {
        const string baseOnly = """
            {
              "$$id": "gts://gts.x.schema12.inst.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": { "a": { "type": "string" } }
            }
            """;
        const string inst = """
            {
              "gtsId": "gts.x.schema12.inst.type.v1~x.y._.i1.v1.0",
              "a": "hi"
            }
            """;
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseOnly)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(inst)!.AsObject()));

        var r = await registry.ValidateSchemaAsync("gts.x.schema12.inst.type.v1~x.y._.i1.v1.0");

        Assert.False(r.Ok);
        Assert.Equal("InvalidSchemaId", r.FailureReason);
    }

    [Fact]
    public async Task Stored_non_schema_under_type_id_returns_not_a_schema()
    {
        const string notSchemaButTypeId = """
            {
              "$$id": "gts://gts.x.schema12.misc.type.v1~",
              "payload": {}
            }
            """;
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(notSchemaButTypeId)!.AsObject()));

        var r = await registry.ValidateSchemaAsync("gts.x.schema12.misc.type.v1~");

        Assert.False(r.Ok);
        Assert.Equal("NotASchema", r.FailureReason);
    }
}
