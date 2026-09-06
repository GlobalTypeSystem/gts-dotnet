namespace Gts.Parsing;

/// <summary>Thrown when GTS ID or pattern parsing fails.</summary>
public class ParseException : Exception
{
    /// <summary>The parse result describing the failure.</summary>
    public ParseResult ParseResult { get; }

    /// <summary>Creates an exception with the given parse result.</summary>
    public ParseException(ParseResult parseResult)
    {
        ParseResult = parseResult;
    }
}