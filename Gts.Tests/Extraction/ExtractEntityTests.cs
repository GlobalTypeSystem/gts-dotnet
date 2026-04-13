using System.Text.Json.Nodes;
using Gts.Extraction;

namespace Gts.Tests.Extraction;

public class ExtractEntityTests
{
    [Fact]
    public void ExtractingPopulatesRefsWithIds()
    {
        var entity = GtsJsonEntity.ExtractEntity(new JsonObject
        {
            ["$id"] = "gts.x.test.core.schema.v1~",
            ["$ref"] = "gts.x.test.core.base.v1~",
        });
        
        Assert.Contains(entity.GtsRefs,
            r => r is { Id: "gts.x.test.core.schema.v1~", SourcePath: "$id" });
    }
    
    [Fact]
    public void ExtractingPopulatesRefsWithExplicitRefs()
    {
        var entity = GtsJsonEntity.ExtractEntity(new JsonObject
        {
            ["$id"] = "gts.x.test.core.schema.v1~",
            ["$ref"] = "gts.x.test.core.base.v1~",
        });
        
        Assert.Contains(entity.GtsRefs,
            r => r is { Id: "gts.x.test.core.base.v1~", SourcePath: "$ref" });
    }
    
    [Fact]
    public void ExtractingPopulatesRefsWithExplicitNestedObjectRefs()
    {
        var entity = GtsJsonEntity.ExtractEntity(new JsonObject
        {
            ["$id"] = "gts.x.test.core.schema.v1~",
            ["properties"] = new JsonObject
            {
                ["field1"] = new JsonObject
                {
                    ["$ref"] = "gts.x.test.core.field.v1~",
                },
            },
        });
        
        Assert.Contains(entity.GtsRefs,
            r => r is { Id: "gts.x.test.core.field.v1~", SourcePath: "properties.field1.$ref" });
    }
    
    [Fact]
    public void ExtractingPopulatesRefsWithExplicitNestedArrayRefs()
    {
        var entity = GtsJsonEntity.ExtractEntity(new JsonObject
        {
            ["$id"] = "gts.x.test.core.schema.v1~",
            ["items"] = new JsonArray
            {
                new JsonObject
                {
                    ["$ref"] = "gts.x.test.core.item.v1~",
                }
            },
        });
        
        Assert.Contains(entity.GtsRefs,
            r => r is { Id: "gts.x.test.core.item.v1~", SourcePath: "items[0].$ref" });
    }

    [Fact]
    public void ExtractingPopulatesRefsFromDoubleDollarRefInAllOf()
    {
        var json = """
            {
              "$$id": "gts://gts.x.test6.events.type.v1~x.test6.rel.missing_base.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "allOf": [ { "$$ref": "gts://gts.x.test6.events.type.v1~" } ]
            }
            """;

        var entity = GtsJsonEntity.ExtractEntity(JsonNode.Parse(json)!.AsObject());

        Assert.Contains(entity.GtsRefs, r => r.Id == "gts.x.test6.events.type.v1~");
    }
}