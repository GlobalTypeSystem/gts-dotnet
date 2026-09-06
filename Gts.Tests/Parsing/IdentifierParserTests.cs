using Gts.Parsing;

namespace Gts.Tests.Parsing;
using Pidgin;

public class IdentifierParserTests
{
    [Fact]
    public void IdentifierParsesOneLetter()
    {
        var ident = Parsers.Identifier.ParseOrThrow("a");
        Assert.Equal("a", ident);
    }

    [Fact]
    public void IdentifierDoesNotParseOneDigit()
    {
        Assert.Throws<ParseException<char>>(
            () => Parsers.Identifier.ParseOrThrow("1"));
    }

    [Fact]
    public void IdentifierParsesLetterFollowedByDigits()
    {
        var ident = Parsers.Identifier.ParseOrThrow("a123");
        Assert.Equal("a123", ident);
    }

    [Fact]
    public void IdentifierParsesUnderscoreFollowedByDigits()
    {
        var ident = Parsers.Identifier.ParseOrThrow("_123");
        Assert.Equal("_123", ident);
    }

    [Fact]
    public void IdentifierDoesNotParseDigitFollowedByLetters()
    {
        Assert.Throws<ParseException<char>>(
            () => Parsers.Identifier.ParseOrThrow("1abc"));
    }

    [Fact]
    public void IdentifierOrWildcardParsesIdentifier()
    {
        var result = Parsers.IdentifierOrWildcard.ParseOrThrow("a");
        Assert.Equal("a", result);
    }

    [Fact]
    public void IdentifierOrWildcardParsesWildcard()
    {
        var result = Parsers.IdentifierOrWildcard.ParseOrThrow("*");
        Assert.Equal("*", result);
    }
}
