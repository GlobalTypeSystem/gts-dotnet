using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Validation;

public class MinorVersionCompatibilityTests
{
    [Fact]
    public void StripLastMinorFromTypeId_strips_only_final_minor()
    {
        Assert.Equal(
            "gts.vendor.pkg.ns.type.v1~",
            GtsTypeFamily.StripLastMinorFromTypeId("gts.vendor.pkg.ns.type.v1.0~"));

        Assert.Equal(
            "gts.base.v1.0~derived.type.v2~",
            GtsTypeFamily.StripLastMinorFromTypeId("gts.base.v1.0~derived.type.v2.3~"));
    }

    [Fact]
    public void AreSameLogicalTypeMinorVariants_requires_explicit_minors_on_last_segment()
    {
        var a = GtsId.Parse("gts.vendor.pkg.ns.type.v1.0~");
        var b = GtsId.Parse("gts.vendor.pkg.ns.type.v1.1~");
        Assert.True(GtsTypeFamily.AreSameLogicalTypeMinorVariants(a, b));

        var majorOnly = GtsId.Parse("gts.vendor.pkg.ns.type.v1~");
        Assert.False(GtsTypeFamily.AreSameLogicalTypeMinorVariants(majorOnly, a));
    }

    [Fact]
    public void CompareSchemas_full_compatibility_when_bodies_match_modulo_minor()
    {
        const string s1 = """
            {
              "$$id": "gts://gts.x.compat.demo.ns.widget.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;

        const string s2 = """
            {
              "$$id": "gts://gts.x.compat.demo.ns.widget.v1.1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;

        var id1 = GtsId.Parse("gts.x.compat.demo.ns.widget.v1.0~");
        var id2 = GtsId.Parse("gts.x.compat.demo.ns.widget.v1.1~");
        var o1 = JsonNode.Parse(s1)!.AsObject();
        var o2 = JsonNode.Parse(s2)!.AsObject();

        var result = GtsSchemaMinorVersionCompatibility.CompareSchemas(id1, o1, id2, o2);
        Assert.True(result.AreCompatible);
    }

    [Fact]
    public void CompareSchemas_detects_structural_drift()
    {
        const string s1 = """
            {
              "$$id": "gts://gts.x.compat.demo.ns.item.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object"
            }
            """;

        const string s2 = """
            {
              "$$id": "gts://gts.x.compat.demo.ns.item.v1.1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["k"]
            }
            """;

        var id1 = GtsId.Parse("gts.x.compat.demo.ns.item.v1.0~");
        var id2 = GtsId.Parse("gts.x.compat.demo.ns.item.v1.1~");

        var result = GtsSchemaMinorVersionCompatibility.CompareSchemas(
            id1,
            JsonNode.Parse(s1)!.AsObject(),
            id2,
            JsonNode.Parse(s2)!.AsObject());

        Assert.False(result.AreCompatible);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task Registry_CheckMinorVersionCompatibilityAsync_reports_incompatible_pair()
    {
        const string s1 = """
            {
              "$$id": "gts://gts.x.compat.reg.ns.doc.v1.0~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object"
            }
            """;

        const string s2 = """
            {
              "$$id": "gts://gts.x.compat.reg.ns.doc.v1.1~",
              "$$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "required": ["x"]
            }
            """;

        var registry = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s1)!.AsObject()));
        await registry.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse(s2)!.AsObject()));

        var report = await registry.CheckMinorVersionCompatibilityAsync();

        Assert.Equal(2, report.SchemaCount);
        Assert.False(report.AreAllCompatible);
        var issue = Assert.Single(report.IncompatiblePairs);
        var ids = new[] { issue.SchemaIdA.Id, issue.SchemaIdB.Id };
        Assert.True(ids.Any(id => id.Contains("v1.0", StringComparison.Ordinal)));
        Assert.True(ids.Any(id => id.Contains("v1.1", StringComparison.Ordinal)));
    }
}
