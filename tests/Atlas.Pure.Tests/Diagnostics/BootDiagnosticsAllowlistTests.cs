using Atlas.Api;
using Atlas.Internal.Diagnostics;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Diagnostics;

public class BootDiagnosticsAllowlistTests
{
    private static readonly BootDiagnosticEntry Warning =
        new(EnumLogType.Warning, "mymod", "boots unconfigured, using defaults", null);

    private static readonly BootDiagnosticEntry Error =
        new(EnumLogType.Error, "unknown", "Syntax error in json file 'mymod:broken.json'", "mymod:broken.json");

    [Fact]
    public void Filter_Should_ReturnEntriesUnchanged_When_NoRulesAreGiven()
    {
        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], []);

        Assert.Equal([Warning, Error], result);
    }

    [Fact]
    public void Filter_Should_RemoveEntry_When_ItsMessageMatchesTheRulePattern()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("^boots unconfigured") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_KeepEntry_When_NoRuleMatchesItsMessage()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("^this never matches$") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning, Error], result);
    }

    [Fact]
    public void Filter_Should_RequireLevelToo_When_RuleDeclaresOne()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "Error") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning], result);
    }

    [Fact]
    public void Filter_Should_RequireSourceToo_When_RuleDeclaresOne()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Source: "mymod") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_MatchTheUnknownSource_When_RuleDeclaresIt()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Source: "unknown") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning], result);
    }

    [Fact]
    public void Filter_Should_RemoveEntry_When_LevelAndSourceAndPatternAllMatch()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured", Level: "Warning", Source: "mymod") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_APatternIsNotValidRegex()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("(unterminated") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Theory]
    [InlineData("Warning")]
    [InlineData("warning")]
    [InlineData("WARNING")]
    [InlineData("WaRnInG")]
    public void Filter_Should_AcceptLevelRegardlessOfCase_When_ItNamesARecordedLevel(string levelSpelling)
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: levelSpelling) };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_AcceptLevelRegardlessOfCase_When_SpellingIsLowercaseError()
    {
        // Different from the Warning-cased theory above: "error" (lowercase) is a real member
        // name that names the OTHER fixture entry, so it removes Error instead of Warning.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "error") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning], result);
    }

    [Fact]
    public void Filter_Should_AcceptLevelRegardlessOfCase_When_SpellingIsUppercaseFatal()
    {
        // No fixture entry is Fatal, so acceptance shows as "nothing removed, no exception",
        // not as a match.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "FATAL") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning, Error], result);
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_LevelNameIsNotRecognized()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "Catastrophic") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_LevelIsMisspelled()
    {
        // A near-miss, not a wildly different word: the case a scenario author actually types by
        // accident, and exactly the shape rejected before and after making Level case-insensitive.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "Warnning") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("9")]
    [InlineData("+8")]
    [InlineData("08")]
    [InlineData("42")]
    [InlineData("Warning,Error")]
    [InlineData("Error,Fatal")]
    [InlineData("Chat,Warning")]
    [InlineData("Warning,Warning")]
    [InlineData(" Warning")]
    [InlineData("Warning ")]
    [InlineData("")]
    public void Filter_Should_ThrowAtlasSetupException_When_LevelIsNotAnExactMemberName(string levelSpelling)
    {
        // Enum.TryParse(ignoreCase: true) alone would accept every one of these: a bare or signed
        // or zero-padded integer (including ones that land on a real member, like 8 for Error), a
        // comma list ORed into a defined member (Error,Fatal -> Fatal) or an undefined one
        // (Warning,Error -> 15), and whitespace it silently trims. Only an exact (case-insensitive)
        // member name may reach the parser at all.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: levelSpelling) };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_NameTheAttributeValueAndDeclaringSite_When_LevelIsRejected()
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic(".*", Level: "Warnning", DeclaredOn: "class 'MyMod.Scenarios'"),
        };

        AtlasSetupException ex =
            Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));

        Assert.Contains("[AtlasAllowBootDiagnostic]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Warnning", ex.Message, StringComparison.Ordinal);
        Assert.Contains("class 'MyMod.Scenarios'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Warning", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Fatal", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_LevelIsBelowWarning()
    {
        // A real member name, just never a level BootDiagnosticsLog would ever record: a rule
        // naming it could never match anything, so it must fail fast rather than compile into a
        // rule that silently does nothing.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*", Level: "Debug") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_ApplyEveryRule_When_SeveralAreGiven()
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic("unconfigured"),
            new AllowedBootDiagnostic("^Syntax error"),
        };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Empty(result);
    }

    [Fact]
    public void Unmet_Should_ReturnNothing_When_NoRuleRequiresAnything()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("^this never matches$") };

        Assert.Empty(BootDiagnosticsAllowlist.Unmet([Warning], rules));
    }

    [Fact]
    public void Unmet_Should_ReturnNothing_When_NoRulesAreGiven()
    {
        Assert.Empty(BootDiagnosticsAllowlist.Unmet([Warning, Error], []));
    }

    [Fact]
    public void Unmet_Should_NameTheRule_When_ARequiredRuleMatchesNothing()
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic("never logged", Level: "Warning", Source: "mymod", DeclaredOn: "class 'MyMod.Scenarios'")
            {
                Required = true,
            },
        };

        string unmet = Assert.Single(BootDiagnosticsAllowlist.Unmet([Warning, Error], rules));

        Assert.Contains("[AtlasAllowBootDiagnostic(\"never logged\"", unmet, StringComparison.Ordinal);
        Assert.Contains("Level = \"Warning\"", unmet, StringComparison.Ordinal);
        Assert.Contains("Source = \"mymod\"", unmet, StringComparison.Ordinal);
        Assert.Contains("Required = true", unmet, StringComparison.Ordinal);
        Assert.Contains("declared on class 'MyMod.Scenarios'", unmet, StringComparison.Ordinal);
        Assert.Contains("matched no boot entry, expected at least one", unmet, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmet_Should_ReturnNothing_When_ARequiredRuleMatchesOnce()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured") { Required = true } };

        Assert.Empty(BootDiagnosticsAllowlist.Unmet([Warning, Error], rules));
    }

    [Fact]
    public void Unmet_Should_ReturnNothing_When_ARequiredRuleMatchesSeveralEntries()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*") { Required = true } };

        Assert.Empty(BootDiagnosticsAllowlist.Unmet([Warning, Error], rules));
    }

    [Fact]
    public void Unmet_Should_NotCountAnEntry_When_ItFailsTheRulesLevelOrSource()
    {
        // The rule's own narrowing applies to what it counts, not only to what it allows.
        AllowedBootDiagnostic[] byLevel = new[] { new AllowedBootDiagnostic(".*", Level: "Fatal") { Required = true } };
        AllowedBootDiagnostic[] bySource = new[] { new AllowedBootDiagnostic(".*", Source: "other") { Required = true } };

        Assert.Single(BootDiagnosticsAllowlist.Unmet([Warning, Error], byLevel));
        Assert.Single(BootDiagnosticsAllowlist.Unmet([Warning, Error], bySource));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Unmet_Should_RequireExactlyCountMatches_When_CountIsSet(int logged, bool met)
    {
        BootDiagnosticEntry[] entries = Enumerable.Repeat(Warning, logged).Append(Error).ToArray();
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured") { Count = 2 } };

        IReadOnlyList<string> unmet = BootDiagnosticsAllowlist.Unmet(entries, rules);

        Assert.Equal(met, unmet.Count == 0);
    }

    [Fact]
    public void Unmet_Should_SayHowManyMatchedAndHowManyWereExpected_When_CountIsNotMet()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured") { Count = 2 } };

        string unmet = Assert.Single(BootDiagnosticsAllowlist.Unmet([Warning], rules));

        Assert.Contains("Count = 2", unmet, StringComparison.Ordinal);
        Assert.Contains("matched 1 boot entry, expected exactly 2", unmet, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmet_Should_ReadTheSurplusAsUnmet_When_MoreEntriesMatchThanCount()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured") { Count = 1 } };

        string unmet = Assert.Single(BootDiagnosticsAllowlist.Unmet([Warning, Warning, Warning], rules));

        Assert.Contains("matched 3 boot entries, expected exactly 1", unmet, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmet_Should_CountAnEntryForEveryRuleThatMatchesIt_When_RulesOverlap()
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic("unconfigured") { Count = 1 },
            new AllowedBootDiagnostic("^boots") { Count = 1 },
        };

        Assert.Empty(BootDiagnosticsAllowlist.Unmet([Warning], rules));
    }

    [Fact]
    public void Unmet_Should_ListEveryUnmetRule_When_SeveralAreUnmet()
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic("first never") { Required = true },
            new AllowedBootDiagnostic("unconfigured") { Required = true },
            new AllowedBootDiagnostic("second never") { Count = 3 },
        };

        IReadOnlyList<string> unmet = BootDiagnosticsAllowlist.Unmet([Warning], rules);

        Assert.Equal(2, unmet.Count);
        Assert.Contains("first never", unmet[0], StringComparison.Ordinal);
        Assert.Contains("second never", unmet[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Unmet_Should_TreatARuleThatTimesOutAsNotMatching_When_ItIsRequired()
    {
        // The same fail-safe as Filter: a pattern that cannot be evaluated never counts as a
        // match, so a required rule built on one reads as unmet rather than silently met.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("(a+)+$") { Required = true } };
        var slow = new BootDiagnosticEntry(EnumLogType.Warning, "mymod", new string('a', 60) + "!", null);

        Assert.Single(BootDiagnosticsAllowlist.Unmet([slow], rules));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unmet_Should_ThrowAtlasSetupException_When_CountIsBelowOne(int count)
    {
        AllowedBootDiagnostic[] rules = new[]
        {
            new AllowedBootDiagnostic(".*", DeclaredOn: "class 'MyMod.Scenarios'") { Count = count },
        };

        AtlasSetupException ex =
            Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Unmet([Warning], rules));

        Assert.Contains("[AtlasAllowBootDiagnostic]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Count", ex.Message, StringComparison.Ordinal);
        Assert.Contains("class 'MyMod.Scenarios'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_CountIsBelowOne()
    {
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic(".*") { Count = 0 } };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_StillAllowTheEntries_When_AMatchedRuleIsUnmet()
    {
        // Required and Count judge the rule; the entries it matches stay allowed either way.
        AllowedBootDiagnostic[] rules = new[] { new AllowedBootDiagnostic("unconfigured") { Count = 5 } };

        Assert.Equal([Error], BootDiagnosticsAllowlist.Filter([Warning, Error], rules));
    }

    [Fact]
    public void Filter_Should_TakeALiteralMessageThroughRegexEscape_When_TheTextHasRegexCharacters()
    {
        // The documented way to allow an exact message: there is no literal option.
        var entry = new BootDiagnosticEntry(EnumLogType.Warning, "mymod", "retrying (1/3) in 5.0s [x]", null);
        string literal = System.Text.RegularExpressions.Regex.Escape("retrying (1/3) in 5.0s [x]");

        Assert.Empty(BootDiagnosticsAllowlist.Filter([entry], new[] { new AllowedBootDiagnostic(literal) }));
        Assert.Single(BootDiagnosticsAllowlist.Filter([entry], new[] { new AllowedBootDiagnostic("retrying (1/3) in 5.0s [x]") }));
    }
}
