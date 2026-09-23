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
        var rules = new[] { new AllowedBootDiagnostic("^boots unconfigured") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_KeepEntry_When_NoRuleMatchesItsMessage()
    {
        var rules = new[] { new AllowedBootDiagnostic("^this never matches$") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning, Error], result);
    }

    [Fact]
    public void Filter_Should_RequireLevelToo_When_RuleDeclaresOne()
    {
        var rules = new[] { new AllowedBootDiagnostic(".*", Level: "Error") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning], result);
    }

    [Fact]
    public void Filter_Should_RequireSourceToo_When_RuleDeclaresOne()
    {
        var rules = new[] { new AllowedBootDiagnostic(".*", Source: "mymod") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_MatchTheUnknownSource_When_RuleDeclaresIt()
    {
        var rules = new[] { new AllowedBootDiagnostic(".*", Source: "unknown") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Warning], result);
    }

    [Fact]
    public void Filter_Should_RemoveEntry_When_LevelAndSourceAndPatternAllMatch()
    {
        var rules = new[] { new AllowedBootDiagnostic("unconfigured", Level: "Warning", Source: "mymod") };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Equal([Error], result);
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_APatternIsNotValidRegex()
    {
        var rules = new[] { new AllowedBootDiagnostic("(unterminated") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_ThrowAtlasSetupException_When_LevelNameIsNotRecognized()
    {
        var rules = new[] { new AllowedBootDiagnostic(".*", Level: "Catastrophic") };

        Assert.Throws<AtlasSetupException>(() => BootDiagnosticsAllowlist.Filter([Warning], rules));
    }

    [Fact]
    public void Filter_Should_ApplyEveryRule_When_SeveralAreGiven()
    {
        var rules = new[]
        {
            new AllowedBootDiagnostic("unconfigured"),
            new AllowedBootDiagnostic("^Syntax error"),
        };

        IReadOnlyList<BootDiagnosticEntry> result = BootDiagnosticsAllowlist.Filter([Warning, Error], rules);

        Assert.Empty(result);
    }
}
