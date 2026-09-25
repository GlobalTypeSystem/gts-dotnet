namespace Gts.Application;

/// <summary>OpenAPI 3.0 document aligned with the gts-go server GetOpenAPISpec helper.</summary>
public static class GtsOpenApiSpec
{
    public static Dictionary<string, object?> Build(string host, int port)
    {
        var baseUrl = $"http://{host}:{port}";
        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.1.0",
            ["info"] = new Dictionary<string, object?>
            {
                ["title"] = "GTS Server",
                ["version"] = "0.1.0",
                ["description"] = "GTS (Global Type System) HTTP API"
            },
            ["servers"] = new object[]
            {
                new Dictionary<string, object?> { ["url"] = baseUrl, ["description"] = "GTS Server" }
            },
            ["paths"] = new Dictionary<string, object?>
            {
                ["/entities"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Get all entities in the registry",
                        ["operationId"] = "getEntities",
                        ["parameters"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["name"] = "limit",
                                ["in"] = "query",
                                ["description"] = "Maximum number of entities to return",
                                ["schema"] = new Dictionary<string, object?> { ["type"] = "integer", ["default"] = 100 }
                            }
                        }
                    },
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Register a single entity (object or schema)",
                        ["operationId"] = "addEntity"
                    }
                },
                ["/validate-id"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Validate a GTS ID format",
                        ["operationId"] = "validateID",
                        ["parameters"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["name"] = "gts_id",
                                ["in"] = "query",
                                ["description"] = "GTS ID to validate",
                                ["required"] = true,
                                ["schema"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        }
                    }
                },
                ["/parse-id"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Parse a GTS ID into its components",
                        ["operationId"] = "parseID",
                        ["parameters"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["name"] = "gts_id",
                                ["in"] = "query",
                                ["description"] = "GTS ID to parse",
                                ["required"] = true,
                                ["schema"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        }
                    }
                },
                ["/match-id-pattern"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Match a GTS ID against a pattern",
                        ["operationId"] = "matchIDPattern"
                    }
                },
                ["/uuid"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Generate UUID from a GTS ID",
                        ["operationId"] = "uuid"
                    }
                },
                ["/validate-instance"] = new Dictionary<string, object?>
                {
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Validate an instance against its schema",
                        ["operationId"] = "validateInstance"
                    }
                },
                ["/validate-schema"] = new Dictionary<string, object?>
                {
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Validate a schema against ref rules and precedent type chain",
                        ["operationId"] = "validateSchema"
                    }
                },
                ["/resolve-relationships"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Resolve relationships for an entity",
                        ["operationId"] = "resolveRelationships"
                    }
                },
                ["/compatibility"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Check compatibility between two schemas",
                        ["operationId"] = "compatibility"
                    }
                },
                ["/cast"] = new Dictionary<string, object?>
                {
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Cast an instance to a target schema",
                        ["operationId"] = "cast"
                    }
                },
                ["/query"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Query entities using an expression",
                        ["operationId"] = "query"
                    }
                },
                ["/attr"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Get attribute value from a GTS entity",
                        ["operationId"] = "attr"
                    }
                },
                ["/type-schemas"] = new Dictionary<string, object?>
                {
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Register a batch of GTS Type Schemas",
                        ["operationId"] = "addTypeSchemas",
                        ["requestBody"] = JsonObjectBody(new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?> { ["type"] = "object" }
                        })
                    }
                },
                ["/validate-json"] = ValidateJsonOperation("Validate transient JSON as a GTS instance or Type Schema"),
                ["/validate-json/{gts_type}"] = new Dictionary<string, object?>
                {
                    ["post"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Validate transient JSON against an explicit GTS Type Schema",
                        ["operationId"] = "validateJsonAsType",
                        ["parameters"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["name"] = "gts_type", ["in"] = "path", ["required"] = true,
                                ["schema"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        },
                        ["requestBody"] = JsonObjectBody(new Dictionary<string, object?> { ["type"] = "object" }),
                        ["responses"] = ValidateJsonResponses()
                    }
                },
                ["/openapi"] = new Dictionary<string, object?>
                {
                    ["get"] = new Dictionary<string, object?>
                    {
                        ["summary"] = "Get the OpenAPI specification for this server",
                        ["operationId"] = "getOpenAPISpec"
                    }
                }
            },
            ["components"] = new Dictionary<string, object?>
            {
                ["schemas"] = new Dictionary<string, object?>
                {
                    ["ValidateJsonResult"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["ok"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                            ["id"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } },
                            ["type_id"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } },
                            ["is_type_schema"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                            ["error"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } }
                        },
                        ["required"] = new[] { "ok", "id", "type_id", "is_type_schema", "error" }
                    }
                }
            }
        };
    }

    private static Dictionary<string, object?> ValidateJsonOperation(string summary) => new()
    {
        ["post"] = new Dictionary<string, object?>
        {
            ["summary"] = summary,
            ["operationId"] = "validateJson",
            ["requestBody"] = JsonObjectBody(new Dictionary<string, object?> { ["type"] = "object" }),
            ["responses"] = ValidateJsonResponses()
        }
    };

    private static Dictionary<string, object?> JsonObjectBody(Dictionary<string, object?> schema) => new()
    {
        ["required"] = true,
        ["content"] = new Dictionary<string, object?>
        {
            ["application/json"] = new Dictionary<string, object?> { ["schema"] = schema }
        }
    };

    private static Dictionary<string, object?> ValidateJsonResponses() => new()
    {
        ["200"] = new Dictionary<string, object?>
        {
            ["description"] = "Validation result",
            ["content"] = new Dictionary<string, object?>
            {
                ["application/json"] = new Dictionary<string, object?>
                {
                    ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/ValidateJsonResult" }
                }
            }
        },
        ["422"] = new Dictionary<string, object?> { ["description"] = "Request body is not a JSON object" }
    };
}
