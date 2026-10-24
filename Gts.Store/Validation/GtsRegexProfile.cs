using System.Text;

namespace Gts.Store.Validation;

/// <summary>
/// The GTS regular-expression profile (README §11.0.1, ADR-0006).
///
/// Checks the shared ECMA-262 <c>u</c>/RE2 syntax subset and support bounds without executing
/// expressions. A dedicated parser is needed because engine parsers (including .NET's) accept
/// extra syntax such as nested classes, set operations, lookaround, and backreferences.
///
/// This is a direct port of the Rust reference implementation (<c>gts/src/regex_profile.rs</c>).
/// </summary>
internal static class GtsRegexProfile
{
    /// <summary>Maximum length in code points after expanding counted repetitions.</summary>
    internal const int MaxExpandedLen = 4096;

    /// <summary>Deepest supported nesting of groups.</summary>
    internal const int MaxGroupNesting = 32;

    /// <summary>Largest supported repetition count, also as the product of nested counts.</summary>
    internal const long MaxRepeat = 1000;

    private const string SyntaxChars = "^$\\.*+?()[]{}|";

    /// <summary>Whether <paramref name="expression"/> is supported, as <c>format: "regex"</c> asserts.</summary>
    internal static bool IsSupported(string expression) => Check(expression) is null;

    /// <summary>
    /// Builds the standard "unsupported regular expression" diagnostic shared by every path that
    /// reports a profile violation, so the wording and truncation live in one place.
    /// <paramref name="location"/> is an optional JSON-pointer-style schema location.
    /// </summary>
    internal static string Describe(string expression, string reason, string? location = null)
    {
        const int shown = 80;
        var display = expression.Length <= shown ? expression : expression[..shown] + "...";
        var at = location is null ? "" : $" at '{location}'";
        return $"unsupported regular expression '{display}'{at}: {reason} " +
               "(GTS regular-expression profile, README section 11.0.1)";
    }

    /// <summary>
    /// Checks that <paramref name="expression"/> is in the profile and within its bounds.
    /// Returns <c>null</c> when supported, otherwise a human-readable reason why it is not.
    /// </summary>
    internal static string? Check(string expression)
    {
        try
        {
            return new Parser(expression).Run();
        }
        catch (UnsupportedException ex)
        {
            return ex.Message;
        }
    }

    private sealed class UnsupportedException(string message) : Exception(message);

    /// <summary>What a parsed node contributes to the support bounds.</summary>
    private readonly struct Size(long expanded, long product)
    {
        /// <summary>Expanded length in code points, saturating.</summary>
        internal long Expanded { get; } = expanded;

        /// <summary>Largest product of counted-repetition factors along a nesting path.</summary>
        internal long Product { get; } = product;

        internal static Size Spelled(long len) => new(len, 1);

        internal Size Then(Size next) => new(
            SaturatingAdd(Expanded, next.Expanded),
            Math.Max(Product, next.Product));
    }

    private static long SaturatingAdd(long a, long b)
    {
        var sum = unchecked(a + b);
        if (((a ^ sum) & (b ^ sum)) < 0)
            return long.MaxValue;
        return sum;
    }

    private static long SaturatingMul(long a, long b)
    {
        try
        {
            return checked(a * b);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private sealed class Parser
    {
        private readonly int[] _chars;
        private int _pos;
        private int _depth;

        internal Parser(string expression)
        {
            // Reject overlong source early: expanded length is at least source length.
            var codePoints = ToCodePoints(expression);
            if (codePoints.Length > MaxExpandedLen)
                throw new UnsupportedException($"it is longer than {MaxExpandedLen} code points");
            _chars = codePoints;
        }

        internal string? Run()
        {
            var size = Disjunction();
            if (_pos < _chars.Length)
                // Only an unmatched ')' stops a top-level disjunction early.
                throw Error($"unmatched '{CharAt(_pos)}'");
            if (size.Expanded > MaxExpandedLen)
                throw new UnsupportedException($"its expanded length exceeds {MaxExpandedLen} code points");
            return null;
        }

        private int? Peek() => _pos < _chars.Length ? _chars[_pos] : null;

        private int? PeekAt(int offset) => _pos + offset < _chars.Length ? _chars[_pos + offset] : null;

        private bool Eat(char c)
        {
            if (Peek() == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private UnsupportedException Error(string reason) =>
            new($"{reason} at offset {_pos}");

        /// <summary>Stops at the end of input or before an unmatched ')'.</summary>
        private Size Disjunction()
        {
            var size = Alternative();
            while (Eat('|'))
                size = size.Then(Size.Spelled(1)).Then(Alternative());
            return size;
        }

        private Size Alternative()
        {
            var size = Size.Spelled(0);
            while (Peek() is { } c)
            {
                Size term;
                if (c is '|' or ')')
                    break;
                if (c is '^' or '$')
                {
                    _pos++;
                    if (Peek() is { } next && IsQuantifierStart(next))
                        throw Error($"quantified assertion '{(char)c}'");
                    term = Size.Spelled(1);
                }
                else
                {
                    var atom = Atom();
                    term = Quantified(atom);
                }
                size = size.Then(term);
                // Bail out early: lengths only grow.
                if (size.Expanded > MaxExpandedLen)
                    throw new UnsupportedException($"its expanded length exceeds {MaxExpandedLen} code points");
            }
            return size;
        }

        private Size Atom()
        {
            if (Peek() is not { } c)
                throw Error("unexpected end");
            switch (c)
            {
                case '(':
                    return Group();
                case '[':
                    return Class();
                case '\\':
                    var start = _pos;
                    _pos++;
                    AtomEscape();
                    return Size.Spelled(_pos - start);
                case '*' or '+' or '?' or '{':
                    throw Error($"quantifier '{(char)c}' without operand");
                case '}' or ']':
                    throw Error($"unescaped '{(char)c}'");
                default:
                    // '.' or a literal.
                    _pos++;
                    return Size.Spelled(1);
            }
        }

        private Size Group()
        {
            var start = _pos;
            _pos++;
            if (Eat('?') && !Eat(':'))
                throw new UnsupportedException(
                    $"only capturing and '(?:' groups are supported, at offset {start}");
            var opening = _pos - start;
            _depth++;
            if (_depth > MaxGroupNesting)
                throw Error($"groups nest deeper than {MaxGroupNesting}");
            var inner = Disjunction();
            if (!Eat(')'))
                throw new UnsupportedException($"unclosed group opened at offset {start}");
            _depth--;
            return Size.Spelled(opening).Then(inner).Then(Size.Spelled(1));
        }

        /// <summary>After '\' outside a class.</summary>
        private void AtomEscape()
        {
            if (Peek() is not { } c)
                throw Error("trailing backslash");
            if (c is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                _pos++;
                return;
            }
            CharEscape(false);
        }

        /// <summary>After '\': an escape denoting one code point, which it returns.</summary>
        private int CharEscape(bool inClass)
        {
            if (Peek() is not { } c)
                throw Error("trailing backslash");
            _pos++;
            switch (c)
            {
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case 'f': return '\f';
                case 'v': return '\v';
                case 'x':
                    var high = HexDigit(PeekAt(0));
                    var low = HexDigit(PeekAt(1));
                    if (high is null || low is null)
                        throw Error("'\\x' takes exactly two hex digits");
                    _pos += 2;
                    return high.Value * 16 + low.Value;
                case '/': return '/';
                case '-' when inClass: return '-';
                default:
                    if (c <= char.MaxValue && SyntaxChars.Contains((char)c))
                        return c;
                    throw Error($"unsupported escape '\\{(char)c}'");
            }
        }

        private static int? HexDigit(int? c)
        {
            if (c is null)
                return null;
            var ch = c.Value;
            if (ch is >= '0' and <= '9')
                return ch - '0';
            if (ch is >= 'a' and <= 'f')
                return ch - 'a' + 10;
            if (ch is >= 'A' and <= 'F')
                return ch - 'A' + 10;
            return null;
        }

        private Size Class()
        {
            var start = _pos;
            _pos++;
            Eat('^');
            if (Peek() == ']')
                throw Error("a class cannot be empty or start with ']'");
            var first = _pos;
            while (true)
            {
                if (Peek() is not { } c)
                    throw new UnsupportedException($"unclosed class opened at offset {start}");
                if (c == ']')
                {
                    _pos++;
                    break;
                }
                if (c == '-' && _pos != first && PeekAt(1) != ']')
                    // A range operator after a shorthand class, or a second one.
                    throw Error("'-' must be the first or last item of a class");
                var low = ClassAtom();
                if (Peek() == '-' && PeekAt(1) is { } next && next != ']')
                {
                    UnambiguousInClass();
                    _pos++;
                    var hi = ClassAtom();
                    if (low is null || hi is null)
                        throw Error("a shorthand class cannot bound a range");
                    if (low.Value > hi.Value)
                        throw Error($"reversed range '{CodePoint(low.Value)}-{CodePoint(hi.Value)}'");
                }
            }
            return Size.Spelled(_pos - start);
        }

        /// <summary>One class item; <c>null</c> for a shorthand class.</summary>
        private int? ClassAtom()
        {
            if (Peek() is not { } c)
                throw Error("unexpected end");
            switch (c)
            {
                case '[':
                    throw Error("unescaped '[' in a class");
                case '\\':
                    _pos++;
                    if (Peek() is { } e && e is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
                    {
                        _pos++;
                        return null;
                    }
                    return CharEscape(true);
                default:
                    UnambiguousInClass();
                    _pos++;
                    return c;
            }
        }

        /// <summary>Reject '&amp;&amp;', '--' and '~~', which Rust reads as class set operations.</summary>
        private void UnambiguousInClass()
        {
            if (Peek() is { } c && c is '&' or '-' or '~' && PeekAt(1) == c)
                throw Error($"ambiguous '{(char)c}{(char)c}' in a class");
        }

        /// <summary>Applies an optional quantifier to an atom of the given size.</summary>
        private Size Quantified(Size atom)
        {
            var start = _pos;
            Size size;
            switch (Peek())
            {
                case '*' or '+' or '?':
                    _pos++;
                    Eat('?');
                    size = Size.Spelled(_pos - start).Then(atom);
                    break;
                case '{':
                    _pos++;
                    var min = Count();
                    long factor;
                    long copies;
                    if (Eat('}'))
                    {
                        factor = min;
                        copies = Math.Max(min, 1);
                    }
                    else if (Eat(','))
                    {
                        if (Eat('}'))
                        {
                            factor = min;
                            copies = min + 1;
                        }
                        else
                        {
                            var max = Count();
                            if (!Eat('}'))
                                throw Error("malformed counted repetition");
                            if (min > max)
                                throw Error($"reversed repetition {{{min},{max}}}");
                            factor = max;
                            copies = Math.Max(max, 1);
                        }
                    }
                    else
                    {
                        throw Error("malformed counted repetition");
                    }
                    Eat('?');
                    var product = SaturatingMul(Math.Max(factor, 1), atom.Product);
                    if (product > MaxRepeat)
                        throw Error($"nested counted repetitions exceed {MaxRepeat} in total");
                    size = new Size(
                        SaturatingAdd(_pos - start, SaturatingMul(copies, atom.Expanded)),
                        product);
                    break;
                default:
                    return atom;
            }
            if (Peek() is { } trailing && IsQuantifierStart(trailing))
                throw Error("stacked or possessive quantifier");
            return size;
        }

        /// <summary>A repetition count of at most <see cref="MaxRepeat"/>.</summary>
        private long Count()
        {
            var start = _pos;
            long value = 0;
            while (Peek() is { } c && c is >= '0' and <= '9')
            {
                value = SaturatingAdd(SaturatingMul(value, 10), c - '0');
                _pos++;
            }
            if (_pos == start)
                throw Error("malformed counted repetition");
            // RE2 reads `{01}` as literal text, not a repetition.
            if (_pos - start > 1 && _chars[start] == '0')
                throw new UnsupportedException($"repetition count at offset {start} has a leading zero");
            if (value > MaxRepeat)
                throw new UnsupportedException(
                    $"repetition count {value} at offset {start} exceeds {MaxRepeat}");
            return value;
        }

        private int CharAt(int pos) => _chars[pos];
    }

    private static bool IsQuantifierStart(int c) => c is '*' or '+' or '?' or '{';

    private static string CodePoint(int value) => char.ConvertFromUtf32(value);

    private static int[] ToCodePoints(string text)
    {
        var points = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                points.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                points.Add(text[i]);
            }
        }
        return points.ToArray();
    }
}
