using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class VersionParserTests
{
    [Fact]
    public void VersionMajorParsesNumberWithVPrefix()
    {
        var major = Parsers.VersionMajor.ParseOrThrow("v123");
        Assert.Equal(123, major);
    }

    [Fact]
    public void VersionMajorDoesNotParseNumberWithoutVPrefix()
    {
        Assert.Throws<ParseException<char>>(() => Parsers.VersionMajor.ParseOrThrow("123"));
    }
    
    [Fact]
    public void VersionMinorParsesNumberWithoutVPrefix()
    {
        var minor = Parsers.VersionMinor.ParseOrThrow("123");
        Assert.Equal(123, minor);
    }

    [Fact]
    public void VersionMinorDoesNotParseNumberWithVPrefix()
    {
        Assert.Throws<ParseException<char>>(() => Parsers.VersionMinor.ParseOrThrow("v123"));
    }

    [Fact]
    public void VersionFullParsesFullVersionString()
    {
        var (major, minor) = Parsers.VersionFull.ParseOrThrow("v123.456");
        Assert.Equal(123, major);
        Assert.Equal(456, minor);
    }

    [Fact]
    public void VersionFullParsesPartialVersionString()
    {
        var (major, minor) = Parsers.VersionFull.ParseOrThrow("v123");
        Assert.Equal(123, major);
        Assert.Null(minor);
    }
}