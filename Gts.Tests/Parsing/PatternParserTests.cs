using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class PatternParserTests
{
    [Fact]
    public void PatternParsesWildcard()
    {
        var segment = Parsers.Pattern.ParseOrThrow("*");
        
        Assert.Null(segment.Vendor);
        Assert.Null(segment.Package);
        Assert.Null(segment.Namespace);
        Assert.Null(segment.Type);
        Assert.Null(segment.Version);
    }
    
    [Fact]
    public void PatternParsesVendor()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.*");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Null(segment.Package);
        Assert.Null(segment.Namespace);
        Assert.Null(segment.Type);
        Assert.Null(segment.Version);
    }

    [Fact]
    public void PatternParsesPackage()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.package.*");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Null(segment.Namespace);
        Assert.Null(segment.Type);
        Assert.Null(segment.Version);
    }

    [Fact]
    public void PatternParsesNamespace()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.package.namespace.*");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Null(segment.Type);
        Assert.Null(segment.Version);
    }

    [Fact]
    public void PatternParsesType()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.package.namespace.type.*");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Equal("type", segment.Type);
        Assert.Null(segment.Version);
    }

    [Fact]
    public void PatternParsesVersion()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.package.namespace.type.v1.0");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Equal("type", segment.Type);

        Assert.NotNull(segment.Version);
        var (major, minor) = segment.Version.Value;
        Assert.Equal(1, major);
        Assert.Equal(0, minor);
    }

    [Fact]
    public void PatternParsesVersionAndTilde()
    {
        var segment = Parsers.Pattern.ParseOrThrow("vendor.package.namespace.type.v1.0~");
        
        Assert.Equal("vendor", segment.Vendor);
        Assert.Equal("package", segment.Package);
        Assert.Equal("namespace", segment.Namespace);
        Assert.Equal("type", segment.Type);

        Assert.NotNull(segment.Version);
        var (major, minor) = segment.Version.Value;
        Assert.Equal(1, major);
        Assert.Equal(0, minor);
    }

    [Fact]
    public void GtsPatternParsesSingleSegment()
    {
        var id = Parsers.GtsPattern.ParseOrThrow("gts.vendor.package.namespace.type.*");
        
        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);
        
        var segments = id.ToArray();
        Assert.Single(segments);
    }

    [Fact]
    public void GtsPatternParsesMultipleSegments()
    {
        var id = Parsers.GtsPattern.ParseOrThrow("gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.*");
        
        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);

        var segments = id.ToArray();
        Assert.Equal(2, segments.Length);
    }

    [Fact]
    public void GtsPatternParsesMultipleWithWildcardAtTheEnd()
    {
        var id = Parsers.GtsPattern.ParseOrThrow("gts.vendor.package.namespace.type.v1.0~*");
        
        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);

        var segments = id.ToArray();
        Assert.Equal(2, segments.Length);
    }

    [Fact]
    public void GtsPatternParsesMultipleWithTildeAtTheEnd()
    {
        var id = Parsers.GtsPattern.ParseOrThrow("gts.vendor.package.namespace.type.v1.0~");
        
        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);

        var segments = id.ToArray();
        Assert.Single(segments);
    }
}