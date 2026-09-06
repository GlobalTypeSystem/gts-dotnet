using System.Text.Json.Nodes;
using Gts;
using Gts.Extraction;
using Gts.Store;

namespace Gts.Tests.Query;

public class GtsQueryTests
{
    [Fact]
    public void TryParse_rejects_filters_on_type_pattern_tilde()
    {
        var ok = GtsQuery.TryParse("""gts.acme.order.ns.invoice.v1~[foo="bar"]""", out _, out var err);
        Assert.False(ok);
        Assert.Contains("filters cannot be used with type patterns", err, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_rejects_filters_on_type_wildcard_tilde_star()
    {
        var ok = GtsQuery.TryParse("gts.acme.order.ns.*~*[foo=bar]", out _, out var err);
        Assert.False(ok);
        Assert.Contains("filters cannot be used with type patterns", err, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_rejects_missing_close_bracket()
    {
        var ok = GtsQuery.TryParse("gts.acme.order.ns.invoice.v1.0[foo=bar", out _, out var err);
        Assert.False(ok);
        Assert.Contains("missing closing bracket", err, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_accepts_instance_with_filters()
    {
        var ok = GtsQuery.TryParse(
            """gts.acme.order.ns.invoice.v1~x.ns._.i1.v1.0[status="active"]""",
            out var q,
            out var err);
        Assert.True(ok);
        Assert.Null(err);
        Assert.NotNull(q);
        Assert.False(q!.IsWildcard);
        Assert.Equal("active", q.Filters["status"]);
    }

    [Fact]
    public async Task QueryAsync_wildcard_and_filter()
    {
        var reg = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        await reg.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
            {
              "gtsId": "gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0",
              "status": "active",
              "name": "A"
            }
            """)!.AsObject()));
        await reg.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
            {
              "gtsId": "gts.acme.order.ns.invoice.v1~x.ns._.b.v1.0",
              "status": "idle",
              "name": "B"
            }
            """)!.AsObject()));

        var r = await reg.QueryAsync("""gts.acme.order.ns.*[status="active"]""", 10);
        Assert.True(r.Ok);
        Assert.Single(r.Results);
        Assert.Equal("active", r.Results[0]["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryAsync_exact_id()
    {
        var reg = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        const string id = "gts.acme.order.ns.invoice.v1~x.ns._.only.v1.0";
        await reg.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse($$"""
            { "gtsId": "{{id}}", "k": 1 }
            """)!.AsObject()));

        var r = await reg.QueryAsync(id, 10);
        Assert.True(r.Ok);
        Assert.Single(r.Results);
        Assert.Equal(1, r.Results[0]["k"]?.GetValue<int>());
    }

    [Fact]
    public async Task QueryAsync_limit_normalized()
    {
        var reg = GtsRegistry.InMemory(new GtsRegistryConfig(false));
        for (var i = 0; i < 5; i++)
        {
            await reg.SaveAsync(GtsJsonEntity.ExtractEntity(JsonNode.Parse($$"""
                { "gtsId": "gts.acme.order.ns.invoice.v1~x.ns._.x{{i}}.v1.0", "n": {{i}} }
                """)!.AsObject()));
        }

        var r = await reg.QueryAsync("gts.acme.order.ns.*", 2);
        Assert.True(r.Ok);
        Assert.Equal(2, r.Results.Count);
        Assert.Equal(2, r.Limit);
    }

    [Fact]
    public void TryFilterIdentifiers_matches_wildcard()
    {
        var ids = new[]
        {
            GtsId.Parse("gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0"),
            GtsId.Parse("gts.vendor.other.ns.widget.v1~x.ns._.b.v1.0")
        };
        var ok = GtsQuery.TryFilterIdentifiers("gts.acme.order.ns.*", ids, 10, out var matched, out var err);
        Assert.True(ok);
        Assert.Null(err);
        Assert.NotNull(matched);
        Assert.Single(matched);
        Assert.Equal("gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0", matched[0].Id);
    }

    [Fact]
    public void TryFilterIdentifiers_rejects_when_filters_present()
    {
        var ids = new[] { GtsId.Parse("gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0") };
        var ok = GtsQuery.TryFilterIdentifiers("gts.acme.order.ns.*[x=1]", ids, 10, out _, out var err);
        Assert.False(ok);
        Assert.Contains("attribute filters require entity JSON", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_uses_star_filter_as_non_empty()
    {
        var entities = new[]
        {
            GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
            { "gtsId": "gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0", "tag": "x" }
            """)!.AsObject()),
            GtsJsonEntity.ExtractEntity(JsonNode.Parse("""
            { "gtsId": "gts.acme.order.ns.invoice.v1~x.ns._.b.v1.0", "tag": "" }
            """)!.AsObject())
        };
        Assert.True(GtsQuery.TryParse("gts.acme.order.ns.*[tag=*]", out var q, out _) && q is not null);
        var hits = GtsQuery.Execute(entities, q, 10);
        Assert.Single(hits);
        Assert.Equal("gts.acme.order.ns.invoice.v1~x.ns._.a.v1.0", hits[0]["gtsId"]?.GetValue<string>());
    }
}
