using Gts.Parsing;
using Pidgin;

namespace Gts.Tests.Parsing;

public class SectionParserTests
{
    [Fact]
    public void GtsParsesLowercaseLiteral()
    {
        var ident = Parsers.GtsPrefix.ParseOrThrow("gts");
        Assert.Equal("gts", ident);
    }

    [Fact]
    public void GtsDoesNotParseUppercaseLiteral()
    {
        Assert.Throws<ParseException<char>>(
            () => Parsers.GtsPrefix.ParseOrThrow("GTS"));
    }
}