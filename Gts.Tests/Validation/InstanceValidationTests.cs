using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Validation;

public class InstanceValidationTests
{
    private static async Task<GtsRegistry> RegistryWithSchemasAndInstanceAsync(
        string baseSchemaJson,
        string derivedSchemaJson,
        string instanceJson)
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(baseSchemaJson)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(derivedSchemaJson)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(instanceJson)!.AsObject()));
        return registry;
    }

    [Fact]
    public async Task Valid_well_known_instance_passes()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["id", "type", "tenantId", "occurredAt"],
              "properties": {
                "type": { "type": "string" },
                "id": { "type": "string" },
                "tenantId": { "type": "string", "format": "uuid" },
                "occurredAt": { "type": "string", "format": "date-time" },
                "payload": { "type": "object" }
              },
              "additionalProperties": false
            }
            """;

        const string derivedSchema = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.test6.events.type.v1~" },
                {
                  "type": "object",
                  "required": ["type", "payload"],
                  "properties": {
                    "type": { "const": "gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~" },
                    "payload": {
                      "type": "object",
                      "required": ["orderId", "customerId", "totalAmount", "items"],
                      "properties": {
                        "orderId": { "type": "string", "format": "uuid" },
                        "customerId": { "type": "string", "format": "uuid" },
                        "totalAmount": { "type": "number" },
                        "items": { "type": "array", "items": { "type": "object" } }
                      }
                    }
                  }
                }
              ]
            }
            """;

        const string instance = """
            {
              "type": "gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~",
              "id": "gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~x.y._.some_event.v1.0",
              "tenantId": "11111111-2222-3333-8444-555555555555",
              "occurredAt": "2025-09-20T18:35:00Z",
              "payload": {
                "orderId": "af0e3c1b-8f1e-4a27-9a9b-b7b9b70c1f01",
                "customerId": "0f2e4a9b-1c3d-4e5f-8a9b-0c1d2e3f4a5b",
                "totalAmount": 149.99,
                "items": [
                  { "sku": "SKU-ABC-001", "name": "Wireless Mouse", "qty": 1, "price": 49.99 }
                ]
              }
            }
            """;

        var registry = await RegistryWithSchemasAndInstanceAsync(baseSchema, derivedSchema, instance);
        var result = await registry.ValidateInstanceAsync(
            "gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~x.y._.some_event.v1.0");

        Assert.True(result.Ok);
        Assert.Equal(
            "gts.x.test6.events.type.v1~x.commerce.orders.order_placed.v1.0~x.y._.some_event.v1.0",
            result.Id);
    }

    [Fact]
    public async Task Invalid_instance_fails_schema_validation()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["id", "type", "tenantId", "occurredAt"],
              "properties": {
                "type": { "type": "string" },
                "id": { "type": "string" },
                "tenantId": { "type": "string", "format": "uuid" },
                "occurredAt": { "type": "string", "format": "date-time" },
                "payload": { "type": "object" }
              },
              "additionalProperties": false
            }
            """;

        const string derivedSchema = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~x.test6.invalid.event.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.test6.events.type.v1~" },
                {
                  "type": "object",
                  "required": ["type", "payload"],
                  "properties": {
                    "type": { "const": "gts.x.test6.events.type.v1~x.test6.invalid.event.v1.0~" },
                    "payload": {
                      "type": "object",
                      "required": ["requiredField"],
                      "properties": {
                        "requiredField": { "type": "string" }
                      }
                    }
                  }
                }
              ]
            }
            """;

        const string instance = """
            {
              "type": "gts.x.test6.events.type.v1~x.test6.invalid.event.v1.0~",
              "id": "gts.x.test6.events.type.v1~x.test6.invalid.event.v1.0~x.y._.some_event2.v1.0",
              "tenantId": "11111111-2222-3333-8444-555555555555",
              "occurredAt": "2025-09-20T18:35:00Z",
              "payload": {
                "someOtherField": "value"
              }
            }
            """;

        var registry = await RegistryWithSchemasAndInstanceAsync(baseSchema, derivedSchema, instance);
        var result = await registry.ValidateInstanceAsync(
            "gts.x.test6.events.type.v1~x.test6.invalid.event.v1.0~x.y._.some_event2.v1.0");

        Assert.False(result.Ok);
        Assert.Equal("SchemaValidationFailed", result.FailureReason);
        Assert.NotNull(result.SchemaErrors);
        Assert.NotEmpty(result.SchemaErrors!);
    }

    [Fact]
    public async Task Missing_instance_returns_not_found()
    {
        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var result = await registry.ValidateInstanceAsync("gts.x.nonexistent.pkg.ns.type.v1.0");
        Assert.False(result.Ok);
        Assert.Equal("InstanceNotFound", result.FailureReason);
    }

    [Fact]
    public async Task Anonymous_instance_validated_by_uuid()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.test6anon.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["id", "type", "tenantId", "occurredAt"],
              "properties": {
                "type": { "type": "string" },
                "id": { "type": "string", "format": "uuid" },
                "tenantId": { "type": "string", "format": "uuid" },
                "occurredAt": { "type": "string", "format": "date-time" },
                "payload": { "type": "object" }
              },
              "additionalProperties": false
            }
            """;

        const string derivedSchema = """
            {
              "$$id": "gts://gts.x.test6anon.events.type.v1~x.commerce.orders.order_placed.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.test6anon.events.type.v1~" },
                {
                  "type": "object",
                  "required": ["type", "payload"],
                  "properties": {
                    "type": { "const": "gts.x.test6anon.events.type.v1~x.commerce.orders.order_placed.v1.0~" },
                    "payload": {
                      "type": "object",
                      "required": ["orderId", "customerId", "totalAmount", "items"],
                      "properties": {
                        "orderId": { "type": "string", "format": "uuid" },
                        "customerId": { "type": "string", "format": "uuid" },
                        "totalAmount": { "type": "number" },
                        "items": { "type": "array", "items": { "type": "object" } }
                      }
                    }
                  }
                }
              ]
            }
            """;

        const string instance = """
            {
              "type": "gts.x.test6anon.events.type.v1~x.commerce.orders.order_placed.v1.0~",
              "id": "7a1d2f34-5678-49ab-9012-abcdef123456",
              "tenantId": "11111111-2222-3333-8444-555555555555",
              "occurredAt": "2025-09-20T18:35:00Z",
              "payload": {
                "orderId": "af0e3c1b-8f1e-4a27-9a9b-b7b9b70c1f01",
                "customerId": "0f2e4a9b-1c3d-4e5f-8a9b-0c1d2e3f4a5b",
                "totalAmount": 149.99,
                "items": [
                  { "sku": "SKU-ABC-001", "name": "Wireless Mouse", "qty": 1, "price": 49.99 }
                ]
              }
            }
            """;

        var registry = await RegistryWithSchemasAndInstanceAsync(baseSchema, derivedSchema, instance);
        var result = await registry.ValidateInstanceAsync("7a1d2f34-5678-49ab-9012-abcdef123456");

        Assert.True(result.Ok);
        Assert.Equal("7a1d2f34-5678-49ab-9012-abcdef123456", result.Id);
    }

    [Fact]
    public async Task Anonymous_invalid_instance_fails()
    {
        const string baseSchema = """
            {
              "$$id": "gts://gts.x.test6anon.events.type.v1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["id", "type", "tenantId", "occurredAt"],
              "properties": {
                "type": { "type": "string" },
                "id": { "type": "string", "format": "uuid" },
                "tenantId": { "type": "string", "format": "uuid" },
                "occurredAt": { "type": "string", "format": "date-time" },
                "payload": { "type": "object" }
              },
              "additionalProperties": false
            }
            """;

        const string derivedSchema = """
            {
              "$$id": "gts://gts.x.test6anon.events.type.v1~x.test6anon.invalid.event.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [
                { "$$ref": "gts://gts.x.test6anon.events.type.v1~" },
                {
                  "type": "object",
                  "required": ["type", "payload"],
                  "properties": {
                    "type": { "const": "gts.x.test6anon.events.type.v1~x.test6anon.invalid.event.v1.0~" },
                    "payload": {
                      "type": "object",
                      "required": ["requiredField"],
                      "properties": {
                        "requiredField": { "type": "string" }
                      }
                    }
                  }
                }
              ]
            }
            """;

        const string instance = """
            {
              "type": "gts.x.test6anon.events.type.v1~x.test6anon.invalid.event.v1.0~",
              "id": "8b2e3f45-6789-4abc-8123-bcdef1234567",
              "tenantId": "11111111-2222-3333-8444-555555555555",
              "occurredAt": "2025-09-20T18:35:00Z",
              "payload": {
                "someOtherField": "value"
              }
            }
            """;

        var registry = await RegistryWithSchemasAndInstanceAsync(baseSchema, derivedSchema, instance);
        var result = await registry.ValidateInstanceAsync("8b2e3f45-6789-4abc-8123-bcdef1234567");

        Assert.False(result.Ok);
        Assert.Equal("SchemaValidationFailed", result.FailureReason);
    }
}
