using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class TypeParserTests
{
    [Fact]
    public void TypeParsesSingleSegment()
    {
        var id = Parsers.GtsTypeId.ParseOrThrow(
            "gts.vendor.package.namespace.type.v1.0~");

        Assert.True(id.IsType);
        Assert.False(id.IsInstance);
        
        var segments = id.ToArray();
        Assert.Single(segments);
    }

    [Fact]
    public void TypeParsesDoubleSegment()
    {
        var id = Parsers.GtsTypeId.ParseOrThrow(
            "gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.v1.0~");

        Assert.True(id.IsType);
        Assert.False(id.IsInstance);

        var segments = id.ToArray();
        Assert.Equal(2, segments.Length);
    }
}