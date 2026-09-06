using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class InstanceParserTests
{
    [Fact]
    public void InstanceParsesDoubleSegment()
    {
        var id = Parsers.GtsInstanceId.ParseOrThrow(
            "gts.vendor.package.namespace.type.v1.0~vendor3.package3.namespace3.type3.v1.0");

        Assert.False(id.IsType);
        Assert.True(id.IsInstance);

        var segments = id.ToArray();
        Assert.Equal(2, segments.Length);
    }
    
    [Fact]
    public void InstanceParsesTripleSegment()
    {
        var id = Parsers.GtsInstanceId.ParseOrThrow(
            "gts.vendor.package.namespace.type.v1.0~vendor3.package3.namespace3.type3.v1.0~vendor3.package3.namespace3.type3.v1.0");

        Assert.False(id.IsType);
        Assert.True(id.IsInstance);

        var segments = id.ToArray();
        Assert.Equal(3, segments.Length);
    }
}