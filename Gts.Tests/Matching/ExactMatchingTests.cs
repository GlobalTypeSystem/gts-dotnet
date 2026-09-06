namespace Gts.Tests.Matching;

public class ExactMatchingTests
{
    [Fact]
    public void MatchesSameInstanceId()
    {
        var id = "gts.vendor.pkg.ns.type.v1.0";
        var candidate = GtsId.Parse(id);
        var pattern = GtsId.ParsePattern(id);

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches(id));
    }

    [Fact]
    public void MatchesSameTypeId()
    {
        var id = "gts.vendor.pkg.ns.type.v1~";
        var candidate = GtsId.Parse(id);
        var pattern = GtsId.ParsePattern(id);

        Assert.True(candidate.Matches(pattern));
        Assert.True(candidate.Matches(id));
    }

    [Fact]
    public void DoesNotMatchDifferentIds()
    {
        var candidate = GtsId.Parse("gts.vendor.pkg.ns.type.v1.0");
        var pattern = GtsId.ParsePattern("gts.other.pkg.ns.type.v1.0");

        Assert.False(candidate.Matches(pattern));
        Assert.False(candidate.Matches("gts.other.pkg.ns.type.v1.0"));
    }
}
