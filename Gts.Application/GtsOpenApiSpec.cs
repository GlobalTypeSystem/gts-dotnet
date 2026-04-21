namespace Gts.Application;

/// <summary>OpenAPI 3.0 document aligned with the gts-go server GetOpenAPISpec helper.</summary>
public static class GtsOpenApiSpec
{
    public static Dictionary<string, object?> Build(string host, int port)
    {
        var baseUrl = $"http://{host}:{port}";
        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.0.0",
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
                }
            }
        };
    }
}
