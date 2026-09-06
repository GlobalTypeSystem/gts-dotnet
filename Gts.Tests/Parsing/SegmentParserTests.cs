using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class SegmentParserTests
{
    [Fact]
    public void SegmentParsesSegmentWithMajorVersion()
    {
        var segment = Parsers.Segment.ParseOrThrow("vendor.package.namespace.type.v1");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Equal("type", segment.Type);
        
        Assert.Equal(1, segment.Version.Value.Major);
        Assert.Null(segment.Version.Value.Minor);
    }
    
    [Fact]
    public void SegmentParsesSegmentWithMajorAndMinorVersion()
    {
        var segment = Parsers.Segment.ParseOrThrow("vendor.package.namespace.type.v1.0");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Equal("type", segment.Type);
        
        Assert.Equal(1, segment.Version.Value.Major);
        Assert.Equal(0, segment.Version.Value.Minor);
    }
}
