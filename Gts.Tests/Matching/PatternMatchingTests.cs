namespace Gts.Tests.Matching;

public class PatternMatchingTests
{
    [Fact]
    public void MatchesWildcardOnMultipleSegments()
    {
        var candidate = GtsId.Parse("gts.x.pkg.events.type.v1~abc.app.events.custom.v1.2");
        var pattern = GtsId.ParsePattern("gts.x.pkg.events.type.v1~abc.*");

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches("gts.x.pkg.events.type.v1~abc.*"));
    }

    [Fact]
    public void MatchesOnMultipleSegmentsWithOnlyWildcard()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v0~a.b.c.d.v1");
        var pattern = GtsId.ParsePattern("gts.vendor.pkg.ns.type.v0~*");

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches("gts.vendor.pkg.ns.type.v0~*"));
    }

    [Fact]
    public void MatchesTypePatternWithAnyMinorVersion()
    {
        var candidate = GtsId.Parse("gts.x.pkg.ns.type.v1.5~");
        var pattern = GtsId.ParsePattern("gts.x.pkg.ns.type.v1~");

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches("gts.x.pkg.ns.type.v1~"));
    }

    [Fact]
    public void DoesNotMatchShorterNonWildcardPattern()
    {
        var candidate = GtsId.Parse("gts.x.pkg.ns.type.v1.5~abc.app.events.custom.v1.2");
        var pattern = GtsId.ParsePattern("gts.x.pkg.ns.type.v1.5");
        
        Assert.False(candidate.Matches(pattern));
        Assert.False(candidate.Matches("gts.x.pkg.ns.type.v1.5"));
    }

    [Fact]
    public void MatchesVendorPrefixWildcard()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v1~");
        var pattern = GtsId.ParsePattern("gts.vendor.*");

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches("gts.vendor.*"));
    }

    [Fact]
    public void MatchesGlobalWildcard()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v1~");
        var pattern = GtsId.ParsePattern("gts.*");

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches("gts.*"));
    }

    [Fact]
    public void DoesNotMatchDifferentMajorVersion()
    {
        var candidate = GtsId.Parse("gts.x.pkg.ns.type.v2~");
        var pattern = GtsId.ParsePattern("gts.x.pkg.ns.type.v1~");

        Assert.False(candidate.Matches(pattern));
        Assert.False(candidate.Matches("gts.x.pkg.ns.type.v1~"));
    }

    [Fact]
    public void DoesNotMatchWhenCandidateShorterThanPatternWithWildcard()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v0~");
        var pattern = GtsId.ParsePattern("gts.vendor.pkg.ns.type.v0~*");

        Assert.False(candidate.Matches(pattern));
        Assert.False(candidate.Matches("gts.vendor.pkg.ns.type.v0~*"));
    }

    [Fact]
    public void DoesNotMatchDifferentVersionWithTrailingWildcard()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v1.1~");
        var pattern = GtsId.ParsePattern("gts.vendor.pkg.ns.type.v0~*");

        Assert.False(candidate.Matches(pattern));
        Assert.False(candidate.Matches("gts.vendor.pkg.ns.type.v0~*"));
    }
}
