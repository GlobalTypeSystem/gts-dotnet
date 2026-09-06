using System.Text.Json.Nodes;
using Gts.Extraction;

namespace Gts.Tests.Extraction;

public class ExtractSchemaTests
{
    [Fact]
    public void ExtractingIdReturnsSchemaFromIdByDefault()
    {
        var gtsId = "gts.vendor.package.namespace.type.v0~a.b.c.d.v1";
        
        var result = GtsJsonEntity.ExtractId(new JsonObject
        {
            ["id"] = gtsId,
            ["name"] = "Some Name"
        });
        
        Assert.Equal(gtsId, result.Id);
        Assert.Equal("gts.vendor.package.namespace.type.v0~", result.SchemaId);
        Assert.Equal("id", result.SelectedSchemaIdField);
    }

    [Theory]
    [InlineData("gtsTid")]
    [InlineData("gtsType")]
    [InlineData("gtsT")]
    [InlineData("gts_t")]
    [InlineData("gts_tid")]
    [InlineData("gts_type")]
    [InlineData("type")]
    [InlineData("schema")]
    public void ExtractingIdReturnsSchemaFromSchemaField(string field)
    {
        var gtsId = "a.b.c.d.v1";
        var schema = "gts.vendor.package.namespace.type.v0~";
        
        var result = GtsJsonEntity.ExtractId(new JsonObject
        {
            ["id"] = gtsId,
            ["name"] = "Some Name",
            [field] = schema,
        });
        
        Assert.Equal(gtsId, result.Id);
        Assert.Equal("gts.vendor.package.namespace.type.v0~", result.SchemaId);
        Assert.Equal(field, result.SelectedSchemaIdField);
    }

    [Fact]
    public void ExtractingDollarSchemaReturnsSchemaFromSpecifiedField()
    {
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                ["id"] = "gts.vendor.package.namespace.type.v1.0~",
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["type"] = "object",
            });
        
        Assert.Equal("gts.vendor.package.namespace.type.v1.0~", result.Id);
        Assert.True(result.IsSchema);
        Assert.Equal("$schema", result.SelectedSchemaIdField);
        Assert.Equal("http://json-schema.org/draft-07/schema#", result.SchemaId);
    }
}
