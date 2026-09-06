namespace Gts.Tests;

public class GtsIdSegmentTests
{
    [Fact]
    public void ToStringForVendorReturnsPattern()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor"
        };

        Assert.Equal("vendor.*", segment.ToString());
    }

    [Fact]
    public void ToStringForVendorAndPackageReturnsPattern()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor",
            Package = "pkg"
        };
        
        Assert.Equal("vendor.pkg.*", segment.ToString());
    }

    [Fact]
    public void ToStringForVendorAndPackageAndNamespaceReturnsPattern()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor",
            Package = "pkg",
            Namespace = "ns"
        };
        
        Assert.Equal("vendor.pkg.ns.*", segment.ToString());
    }

    [Fact]
    public void ToStringForVendorAndPackageAndNamespaceAndTypeReturnsPattern()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor",
            Package = "pkg",
            Namespace = "ns",
            Type = "type"
        };
        
        Assert.Equal("vendor.pkg.ns.type.*", segment.ToString());
    }

    [Fact]
    public void ToStringForVendorAndPackageAndNamespaceAndTypeAndMajorReturnsId()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor",
            Package = "pkg",
            Namespace = "ns",
            Type = "type",
            VersionMajor = 1,
        };
        
        Assert.Equal("vendor.pkg.ns.type.v1", segment.ToString());
    }

    [Fact]
    public void ToStringForVendorAndPackageAndNamespaceAndTypeAndMajorAndMinorReturnsId()
    {
        var segment = new GtsIdSegment
        {
            Vendor = "vendor",
            Package = "pkg",
            Namespace = "ns",
            Type = "type",
            VersionMajor = 1,
            VersionMinor = 2,
        };
        
        Assert.Equal("vendor.pkg.ns.type.v1.2", segment.ToString());
    }
}
