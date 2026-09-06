using System.Text.Json.Nodes;
using Gts.Extraction;

namespace Gts.Tests.Extraction;

public class ExtractBasicTests
{
    [Theory]
    [InlineData("$id")]
    [InlineData("gtsId")]
    [InlineData("gtsIid")]
    [InlineData("gtsOid")]
    [InlineData("gtsI")]
    [InlineData("gts_id")]
    [InlineData("gts_oid")]
    [InlineData("gts_iid")]
    [InlineData("id")]
    public void ExtractingOneOfDefaultIdsReturnsId(string id)
    {
        var gtsId = "gts.vendor.package.namespace.type.v0~a.b.c.d.v1";
        
        var result = GtsJsonEntity.ExtractId(new JsonObject
        {
            [id] = gtsId,
            ["name"] = "Some Name"
        });
        
        Assert.Equal(id, result.SelectedEntityField);
        Assert.Equal(gtsId, result.Id);
    }

    [Theory]
    [InlineData("invalid_id")]
    public void ExtractingOneOfNonDefaultIdsReturnsNulls(string id)
    {
        var gtsId = "gts.vendor.package.namespace.type.v0~a.b.c.d.v1";
        
        var result = GtsJsonEntity.ExtractId(new JsonObject
        {
            [id] = gtsId,
            ["name"] = "Some Name",
        });
        
        Assert.Null(result.SelectedEntityField);
        Assert.Null(result.Id);
    }

    [Theory]
    [InlineData("custom_id")]
    public void ExtractingOneOfCustomIdsWithConfigReturnsId(string id)
    {
        var gtsId = "gts.vendor.package.namespace.type.v0~a.b.c.d.v1";
        
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                [id] = gtsId,
                ["name"] = "Some Name"
            },
            new (){ EntityIdPropertyNames = [id]});
        
        Assert.Equal(id, result.SelectedEntityField);
        Assert.Equal(gtsId, result.Id);
    }

    [Fact]
    public void ExtractingOneOfDefaultIdsReturnsIdsByOrdering()
    {
        var gtsId = "gts.vendor.package.namespace.type.v0~a.b.c.d.v1";
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                ["name"] = "Some Name",
                ["gtsId"] = gtsId, // 1st
                ["$id"] = gtsId, // 2nd
            });
        
        Assert.Equal(gtsId, result.Id);
        Assert.Equal("gtsId", result.SelectedEntityField); // 1st wins
    }

    [Fact]
    public void ExtractingDollarIdReturnsIdWithStrippedPrefix()
    {
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                ["$id"] = "gts://gts.vendor.package.namespace.type.v1.0~",
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["type"] = "object",
            });
        
        Assert.Equal("gts.vendor.package.namespace.type.v1.0~", result.Id);
    }

    [Fact]
    public void ExtractingInvalidIdFallbacksToNextValidId()
    {
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                ["gtsId"] = "invalid-id", // invalid
                ["name"] = "Some Name",
                ["id"] = "gts.vendor.package.namespace.type.v1.0~", // valid
            });
        
        Assert.Equal("gts.vendor.package.namespace.type.v1.0~", result.Id);
        Assert.Equal("id", result.SelectedEntityField);
    }

    [Fact]
    public void ExtractingIdReturnsNullWhenValidIdDoesNotExist()
    {
        var result = GtsJsonEntity.ExtractId(
            new JsonObject
            {
                ["name"] = "Some Name",
            });
        
        Assert.Null(result.Id);
        Assert.Null(result.SelectedEntityField);
    }
}