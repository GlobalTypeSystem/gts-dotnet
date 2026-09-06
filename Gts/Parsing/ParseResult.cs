namespace Gts.Parsing;

/// <summary>Result of a GTS parse attempt; use implicit conversion to bool for success check.</summary>
public sealed class ParseResult
{
    /// <summary>Indicates successful parse.</summary>
    public static readonly ParseResult Success = new();
    /// <summary>Indicates null input was passed.</summary>
    public static readonly ParseResult ArgumentIsNull = new(); // TODO: actual error message
    
    /// <summary>Returns true when this result represents success.</summary>
    public static implicit operator bool(ParseResult result)
    {
        return result == Success;
    }
}
