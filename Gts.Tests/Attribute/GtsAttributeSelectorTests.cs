using System.Text.Json.Nodes;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Attribute;

public class GtsAttributeSelectorTests
{
    private static readonly JsonObject Sample = JsonNode.Parse("""
        {
          "id": "gts.x.msg.v1~x._.m.v1.0",
          "foo": { "bar": 42 },
          "items": [ { "a": 1 }, { "a": 2 } ]
        }
        """)!.AsObject();

    [Fact]
    public void TryResolve_nested_property()
    {
        Assert.True(GtsAttributeSelector.TryResolve(Sample, "foo.bar", out var v));
        Assert.Equal(42, v!.GetValue<int>());
    }

    [Fact]
    public void TryResolve_slash_equals_dot()
    {
        Assert.True(GtsAttributeSelector.TryResolve(Sample, "foo/bar", out var v));
        Assert.Equal(42, v!.GetValue<int>());
    }

    [Fact]
    public void TryResolve_chained_array_indices()
    {
        Assert.True(GtsAttributeSelector.TryResolve(Sample, "items[0].a", out var v));
        Assert.Equal(1, v!.GetValue<int>());
    }

    [Fact]
    public void TryResolve_segment_foo_bracket_0_bracket_1_style()
    {
        var nested = JsonNode.Parse("""{ "m": [ [ 10, 20 ], [ 30, 40 ] ] }""")!.AsObject();
        Assert.True(GtsAttributeSelector.TryResolve(nested, "m[0][1]", out var v));
        Assert.Equal(20, v!.GetValue<int>());
    }

    [Fact]
    public void TryResolve_empty_path_returns_root()
    {
        Assert.True(GtsAttributeSelector.TryResolve(Sample, "", out var v));
        Assert.Same(Sample, v);
    }

    [Fact]
    public void TryResolvePath_sets_available_fields_on_missing_key()
    {
        var ok = GtsAttributeSelector.TryResolvePath(Sample, "notAProperty", out _, out var err, out var fields);
        Assert.False(ok);
        Assert.NotNull(err);
        Assert.NotNull(fields);
        Assert.Contains("foo.bar", fields);
        Assert.Contains("id", fields);
    }

    [Fact]
    public async Task GetAttributeAsync_resolves_stored_entity()
    {
        var reg = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await reg.SaveAsync(GtsJsonEntity.ExtractEntity(Sample));

        var r = await reg.GetAttributeAsync("gts.x.msg.v1~x._.m.v1.0@foo.bar");
        Assert.True(r.Resolved);
        Assert.Equal(42, r.Value!.GetValue<int>());
    }

    [Fact]
    public async Task GetAttributeAsync_requires_at()
    {
        var reg = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        var r = await reg.GetAttributeAsync("gts.x.msg.v1~x._.m.v1.0");
        Assert.False(r.Resolved);
        Assert.Contains("@path", r.Error ?? "", StringComparison.Ordinal);
    }
}
