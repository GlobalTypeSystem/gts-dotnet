using Gts.Store.Validation;

namespace Gts.Tests.Validation;

/// <summary>Changes the process-wide NonBacktracking automaton limit, so it runs alone.</summary>
[CollectionDefinition(nameof(RegexAutomataLimitCollection), DisableParallelization = true)]
public class RegexAutomataLimitCollection;

[Collection(nameof(RegexAutomataLimitCollection))]
public class RegexCompilerTests
{
    private const string MaxAutomataSizeSwitch = "REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE";

    // gts-spec `test_regex_validation.py` expressions at the common support bounds; .NET 8 refused
    // them with its default 1,000-node automaton limit.
    [Theory]
    [InlineData("a{1000}")]
    [InlineData("(?:a{10}){100}")]
    [InlineData("a{1000,}")]
    [InlineData(@"\S{1000}\W{1000}")]
    [InlineData(@"[^\s\d]{580}")]
    [InlineData("^(?:a?){500}a{500}$")]
    // Measured as the largest in-profile automata: `.` and an astral negated class at the bounds.
    [InlineData(".{1000}.{1000}.{1000}.{1000}")]
    [InlineData("[^😀]{1000}")]
    public void Expressions_at_the_profile_bounds_compile(string expression)
    {
        var regex = GtsRegexCompiler.Compile(expression);

        _ = regex.IsMatch(new string('a', 4000));
    }

    [Fact]
    public void An_automaton_over_the_engine_limit_is_an_unsupported_expression()
    {
        _ = GtsRegexCompiler.Compile("a");
        var configured = AppContext.GetData(MaxAutomataSizeSwitch);
        AppContext.SetData(MaxAutomataSizeSwitch, 100);
        try
        {
            var exception = Assert.Throws<GtsRegexException>(() => GtsRegexCompiler.Compile("b{999}"));
            Assert.Contains("engine failed to compile it", exception.Message);
        }
        finally
        {
            AppContext.SetData(MaxAutomataSizeSwitch, configured);
        }
    }
}
