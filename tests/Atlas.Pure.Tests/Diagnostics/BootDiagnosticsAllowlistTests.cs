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
}
