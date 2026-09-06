using Gts.Parsing;
using Gts.Utils;

namespace Gts.Tests;

public class GtsIdTests
{
    [Fact]
    public void CanBeParsedAsSingleSegmentType()
    {
        var id = GtsId.Parse("gts.vendor.package.namespace.type.v1.0~");

        Assert.True(id.IsType);
        Assert.Single(id.Segments);
    }

    [Fact]
    public void CanBeParsedAsMultipleSegmentsType()
    {
        var id = GtsId.Parse("gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.v1.0~");
        
        Assert.True(id.IsType);
        Assert.Equal(2, id.Segments.Count);
    }

    [Fact]
    public void CanBeParsedAsMultipleSegmentsInstance()
    {
        var id = GtsId.Parse("gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.v1.0");

        Assert.True(id.IsInstance);
        Assert.Equal(2, id.Segments.Count);
    }
    
    [Fact]
    public void CanBeParsedAsSingleSegmentPattern()
    {
        var id = GtsId.ParsePattern("gts.vendor.package.namespace.type.*");

        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);
        Assert.Single(id.Segments);
    }

    [Fact]
    public void CanBeParsedAsMultipleSegmentsPattern()
    {
        var id = GtsId.ParsePattern("gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.*");
        
        Assert.False(id.IsType);
        Assert.False(id.IsInstance);
        Assert.True(id.IsPattern);
        Assert.Equal(2, id.Segments.Count);
    }
    
    [Fact]
    public void CannotBeParsedWithTrailingStringCharacters()
    {
        var str = "gts.vendor.package.namespace.type.v1.0~ ";
        
        Assert.False(GtsId.TryParse(str, out _));
        Assert.Throws<ParseException>(() => GtsId.Parse(str));
        
        Assert.False(GtsId.TryParsePattern(str, out _));
        Assert.Throws<ParseException>(() => GtsId.ParsePattern(str));
    }
    
    [Fact]
    public void CannotBeParsedWithNullAsInput()
    {
        string? str = null;
        
        Assert.False(GtsId.TryParse(str, out _));
        Assert.Throws<ParseException>(() => GtsId.Parse(str));
        
        Assert.False(GtsId.TryParsePattern(str, out _));
        Assert.Throws<ParseException>(() => GtsId.ParsePattern(str));
    }

    [Fact]
    public void ToGuidProducesGuidInGtsNamespace()
    {
        var id = GtsId.Parse("gts.vendor.package.namespace.type.v1.0~");
        var guid = id.ToGuid();
        
        Assert.Equal(GuidUtils.Create(GuidUtils.GtsNamespace, id.Id), guid);
    }

    [Fact]
    public void ToStringReturnsId()
    {
        var idStr = "gts.vendor.package.namespace.type.v1.0~";
        var id = GtsId.Parse(idStr);
        Assert.Equal(idStr, id.ToString());
        Assert.Equal(id.Id, id.ToString());
    }

    [Fact]
    public void ToStringReturnsPattern()
    {
        var patternStr = "gts.vendor.package.namespace.type.*";
        var pattern = GtsId.ParsePattern(patternStr);
        Assert.Equal(patternStr, pattern.ToString());
        Assert.Equal(pattern.Id, pattern.ToString());
    }

    [Fact]
    public void GetHashCodeIsTheSameAsGetHashCodeForString()
    {
        var str = "gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.v1.0";
        var id = GtsId.Parse(str);
        
        Assert.Equal(
            StringComparer.Ordinal.GetHashCode(str),
            id.GetHashCode());
    }

    [Fact]
    public void EqualsIsTheSameAsEqualsForString()
    {
        var str1 = "gts.vendor.package.namespace.type.v1.0~vendor1.package1.namespace1.type1.v1.0";
        var str2 = "gts.vendor.package.namespace.type.v1.0~vendor2.package2.namespace2.type2.v1.0";
        
        var id1 = GtsId.Parse(str1);
        var id12 = GtsId.Parse(str1);
        var id2 = GtsId.Parse(str2);
        
        Assert.Equal(id1, id12);
        Assert.NotEqual(id1, id2);
    }
}