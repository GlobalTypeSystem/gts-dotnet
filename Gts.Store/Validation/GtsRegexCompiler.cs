using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Gts.Store.Validation;

/// <summary>
/// Compiles GTS-profile regular expressions (README §11.0.1, ADR-0006) for matching.
///
/// An expression is first checked against <see cref="GtsRegexProfile"/>; an out-of-profile or
/// over-bounds expression is unsupported and rejected at the schema position (never a silent
/// non-match). A supported expression is translated to the reference matching semantics and
/// compiled with <see cref="RegexOptions.NonBacktracking"/>, which guarantees the linear-time
/// Boolean search ADR-0006 requires and rejects exactly the lookaround/backreference constructs
/// the profile already excludes.
///
/// Reference matching uses RE2 semantics (unanchored search; case sensitive; multiline and dot-all
/// off; <c>$</c> matches end of input only, not before a final LF; atoms match code points;
/// <c>\d</c>, <c>\w</c>, <c>\s</c> are ASCII). No permitted deviation is declared, so the
/// expression is translated to these semantics (see <c>Translate</c>).
/// </summary>
internal static class GtsRegexCompiler
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>AppContext switch for the largest automaton the NonBacktracking engine builds.</summary>
    private const string MaxAutomataSizeSwitch = "REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE";

    /// <summary>
    /// Estimated automaton nodes the engine must accept for every expression within the profile
    /// bounds, with headroom: the largest translations measured need about 12,200 (<c>.</c>
    /// repeated to the expanded-length bound, each a code-point alternation), above the engine
    /// default (1,000 on .NET 8, 10,000 on .NET 9+).
    /// </summary>
    private const int MaxAutomataSize = GtsRegexProfile.MaxExpandedLen * 10;

    static GtsRegexCompiler()
    {
        // Read on every construction; raised only, so a larger host setting is kept.
        if (AppContext.GetData(MaxAutomataSizeSwitch) is not int configured || configured < MaxAutomataSize)
            AppContext.SetData(MaxAutomataSizeSwitch, MaxAutomataSize);
    }

    /// <summary>
    /// Compiles <paramref name="expression"/> for matching, throwing when it is unsupported or the
    /// engine cannot build it.
    /// </summary>
    /// <exception cref="GtsRegexException">The expression is outside the profile or its bounds, or
    /// an in-profile expression the engine could not compile (an engine failure, not a non-match).</exception>
    internal static Regex Compile(string expression)
    {
        if (Cache.Count >= 1024)
            Cache.Clear();
        return Cache.GetOrAdd(expression, static expr =>
        {
            var reason = GtsRegexProfile.Check(expr);
            if (reason is not null)
                throw new GtsRegexException(GtsRegexProfile.Describe(expr, reason));
            try
            {
                return new Regex(Translate(expr), RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
            }
            // NotSupportedException: the estimated automaton exceeds MaxAutomataSize.
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new GtsRegexException(
                    GtsRegexProfile.Describe(expr, $"engine failed to compile it: {ex.Message}"));
            }
        });
    }

    /// <summary>Whether <paramref name="expression"/> is in the profile (as <c>format: "regex"</c> asserts).</summary>
    internal static bool IsSupported(string expression) => GtsRegexProfile.IsSupported(expression);

    private static readonly (int Lo, int Hi)[] Digit = [('0', '9')];
    private static readonly (int Lo, int Hi)[] Word = [('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z')];
    private static readonly (int Lo, int Hi)[] Space = [('\t', '\n'), ('\f', '\r'), (' ', ' ')];
    private static readonly (int Lo, int Hi)[] Dot = [(0, '\n' - 1), ('\n' + 1, 0x10FFFF)];

    /// <summary>
    /// Translates a profile expression to an equivalent .NET expression with reference semantics:
    /// atoms match whole code points (astral characters become surrogate-pair alternations),
    /// <c>\d</c>, <c>\w</c>, <c>\s</c> are the ASCII/RE2 sets, <c>.</c> matches any code point but
    /// LF, and <c>$</c> becomes <c>\z</c>. The profile guarantees the input is valid.
    /// </summary>
    private static string Translate(string expression)
    {
        var cps = ToCodePoints(expression);
        var builder = new StringBuilder(expression.Length * 2);
        var i = 0;
        while (i < cps.Length)
        {
            var c = cps[i];
            switch (c)
            {
                case '^' or ')' or '|' or '*' or '+' or '?':
                    builder.Append((char)c);
                    i++;
                    break;
                case '$':
                    builder.Append(@"\z");
                    i++;
                    break;
                case '(':
                    if (i + 1 < cps.Length && cps[i + 1] == '?')
                    {
                        builder.Append("(?:");
                        i += 3;
                    }
                    else
                    {
                        builder.Append('(');
                        i++;
                    }
                    break;
                case '{':
                    while (cps[i] != '}')
                        builder.Append((char)cps[i++]);
                    builder.Append('}');
                    i++;
                    break;
                case '.':
                    Emit(builder, Dot);
                    i++;
                    break;
                case '[':
                    i++;
                    Emit(builder, ParseClass(cps, ref i));
                    break;
                case '\\':
                    i++;
                    if (Shorthand(cps[i]) is { } shorthand)
                    {
                        Emit(builder, shorthand);
                        i++;
                    }
                    else
                    {
                        var literal = Escape(cps, ref i);
                        Emit(builder, [(literal, literal)]);
                    }
                    break;
                default:
                    Emit(builder, [(c, c)]);
                    i++;
                    break;
            }
        }
        return builder.ToString();
    }

    /// <summary>The set of a shorthand escape letter, or <c>null</c> when it is not one.</summary>
    private static List<(int Lo, int Hi)>? Shorthand(int c) => c switch
    {
        'd' => [.. Digit],
        'D' => Complement(Digit),
        'w' => [.. Word],
        'W' => Complement(Word),
        's' => [.. Space],
        'S' => Complement(Space),
        _ => null,
    };

    /// <summary>Reads a single-code-point escape starting after '\'; leaves <paramref name="i"/> after it.</summary>
    private static int Escape(int[] cps, ref int i)
    {
        var c = cps[i++];
        switch (c)
        {
            case 'n': return '\n';
            case 'r': return '\r';
            case 't': return '\t';
            case 'f': return '\f';
            case 'v': return '\v';
            case 'x':
                var value = Convert.ToInt32(new string([(char)cps[i], (char)cps[i + 1]]), 16);
                i += 2;
                return value;
            default: return c;
        }
    }

    /// <summary>Parses a class body after '['; leaves <paramref name="i"/> after the closing ']'.</summary>
    private static List<(int Lo, int Hi)> ParseClass(int[] cps, ref int i)
    {
        var negated = false;
        if (cps[i] == '^')
        {
            negated = true;
            i++;
        }
        var set = new List<(int Lo, int Hi)>();
        while (cps[i] != ']')
        {
            int low;
            if (cps[i] == '\\')
            {
                i++;
                if (Shorthand(cps[i]) is { } shorthand)
                {
                    set.AddRange(shorthand);
                    i++;
                    continue;
                }
                low = Escape(cps, ref i);
            }
            else
            {
                low = cps[i++];
            }
            var high = low;
            if (cps[i] == '-' && cps[i + 1] != ']')
            {
                i++;
                if (cps[i] == '\\')
                {
                    i++;
                    high = Escape(cps, ref i);
                }
                else
                {
                    high = cps[i++];
                }
            }
            set.Add((low, high));
        }
        i++;
        return negated ? Complement(set) : Normalize(set);
    }

    private static List<(int Lo, int Hi)> Normalize(IEnumerable<(int Lo, int Hi)> ranges)
    {
        var result = new List<(int Lo, int Hi)>();
        foreach (var (lo, hi) in ranges.OrderBy(r => r.Lo))
        {
            if (result.Count > 0 && lo <= result[^1].Hi + 1)
                result[^1] = (result[^1].Lo, Math.Max(result[^1].Hi, hi));
            else
                result.Add((lo, hi));
        }
        return result;
    }

    private static List<(int Lo, int Hi)> Complement(IEnumerable<(int Lo, int Hi)> ranges)
    {
        var result = new List<(int Lo, int Hi)>();
        var next = 0;
        foreach (var (lo, hi) in Normalize(ranges))
        {
            if (lo > next)
                result.Add((next, lo - 1));
            next = hi + 1;
        }
        if (next <= 0x10FFFF)
            result.Add((next, 0x10FFFF));
        return result;
    }

    /// <summary>Emits a code-point set as a single .NET atom over UTF-16 code units.</summary>
    private static void Emit(StringBuilder builder, IEnumerable<(int Lo, int Hi)> ranges)
    {
        var bmp = new StringBuilder();
        var alternatives = new List<string>();
        foreach (var (rangeLo, rangeHi) in Normalize(ranges))
        {
            // Surrogate code points cannot be matched as scalar values; exclude them.
            foreach (var (lo, hi) in new[] { (rangeLo, Math.Min(rangeHi, 0xD7FF)), (Math.Max(rangeLo, 0xE000), Math.Min(rangeHi, 0xFFFF)) })
            {
                if (lo > hi)
                    continue;
                bmp.Append(Unit(lo));
                if (hi > lo)
                    bmp.Append('-').Append(Unit(hi));
            }
            if (rangeHi < 0x10000)
                continue;
            var start = Math.Max(rangeLo, 0x10000);
            var (hsLo, lsLo) = Surrogates(start);
            var (hsHi, lsHi) = Surrogates(rangeHi);
            if (hsLo == hsHi)
            {
                alternatives.Add($"{Unit(hsLo)}[{Unit(lsLo)}-{Unit(lsHi)}]");
                continue;
            }
            // Partial leading/trailing high surrogates get their own alternative; full ones share one.
            var fullLo = hsLo;
            var fullHi = hsHi;
            if (lsLo != 0xDC00)
                alternatives.Add($"{Unit(fullLo++)}[{Unit(lsLo)}-\\uDFFF]");
            if (lsHi != 0xDFFF)
                alternatives.Add($"{Unit(fullHi--)}[\\uDC00-{Unit(lsHi)}]");
            if (fullLo <= fullHi)
                alternatives.Add($"[{Unit(fullLo)}-{Unit(fullHi)}][\\uDC00-\\uDFFF]");
        }
        if (bmp.Length > 0)
            alternatives.Insert(0, $"[{bmp}]");
        if (alternatives.Count == 0)
            builder.Append(@"[^\u0000-\uFFFF]");
        else if (alternatives.Count == 1 && bmp.Length > 0)
            builder.Append(alternatives[0]);
        else
            builder.Append("(?:").Append(string.Join('|', alternatives)).Append(')');
    }

    private static (int High, int Low) Surrogates(int codePoint)
    {
        var offset = codePoint - 0x10000;
        return (0xD800 + (offset >> 10), 0xDC00 + (offset & 0x3FF));
    }

    private static string Unit(int value) => $"\\u{value:X4}";

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

/// <summary>Raised when a regular expression is outside the GTS profile or the engine fails on it.</summary>
internal sealed class GtsRegexException(string message) : Exception(message);
