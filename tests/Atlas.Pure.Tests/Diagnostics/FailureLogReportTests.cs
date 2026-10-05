using Atlas.Api;
using Atlas.Internal.Diagnostics;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Diagnostics;

public class FailureLogReportTests
{
    private const string DataPath = "/scratch/atlas/deadbeef";

    [Fact]
    public void Describe_Should_NameTheServerLog_When_NoErrorWasLogged()
    {
        string report = FailureLogReport.Describe(DataPath, []);

        Assert.Equal($"[Atlas] server log: {Path.Combine(DataPath, "Logs", "server-main.log")}", report);
    }

    [Fact]
    public void Describe_Should_IgnoreWarnings_When_OnlyWarningsWereLogged()
    {
        string report = FailureLogReport.Describe(DataPath, [Entry(EnumLogType.Warning, "mod", "just a warning")]);

        Assert.DoesNotContain("just a warning", report, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', report);
    }

    [Fact]
    public void Describe_Should_ListErrorAndFatalEntriesWithTheirSource_When_TheyWereLogged()
    {
        string report = FailureLogReport.Describe(
            DataPath,
            [
                Entry(EnumLogType.Error, "mymod", "could not bind port 39484"),
                Entry(EnumLogType.Fatal, "unknown", "the game is going down"),
            ]);

        string[] lines = report.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("[Atlas] server log: ", lines[0], StringComparison.Ordinal);
        Assert.Equal("[Atlas] 2 error(s) logged by the engine since the boot:", lines[1]);
        Assert.Equal("  Error [mymod] could not bind port 39484", lines[2]);
        Assert.Equal("  Fatal [unknown] the game is going down", lines[3]);
    }

    [Fact]
    public void Describe_Should_ShowTheHint_When_TheSourceIsUnknownButHinted()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", "failed to load", null, "bindingfixture");

        string report = FailureLogReport.Describe(DataPath, [entry]);

        Assert.Contains("  Error [unknown, hint bindingfixture] failed to load", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_KeepOnlyTheFirstLineOfAMessage_When_ItCarriesAStackTrace()
    {
        string report = FailureLogReport.Describe(
            DataPath,
            [Entry(EnumLogType.Error, "mymod", "Exception: boom\r\n   at My.Mod.Start()\n   at Engine.Run()")]);

        Assert.Contains("  Error [mymod] Exception: boom", report, StringComparison.Ordinal);
        Assert.DoesNotContain("My.Mod.Start", report, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', report);
    }

    [Fact]
    public void Describe_Should_CutALongLine_When_AMessageIsLong()
    {
        string report = FailureLogReport.Describe(DataPath, [Entry(EnumLogType.Error, "mymod", new string('x', 500))]);

        string line = report.Split('\n')[2];
        Assert.Equal(FailureLogReport.MaxLineLength, line.Length);
        Assert.EndsWith("...", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_ListTheFirstEntriesAndCountTheRest_When_MoreThanTheBoundWereLogged()
    {
        IReadOnlyList<BootDiagnosticEntry> entries =
            [.. Enumerable.Range(1, FailureLogReport.MaxEntries + 3).Select(i => Entry(EnumLogType.Error, "mymod", $"error {i}"))];

        string report = FailureLogReport.Describe(DataPath, entries);

        string[] lines = report.Split('\n');
        Assert.Equal($"[Atlas] {FailureLogReport.MaxEntries + 3} error(s) logged by the engine since the boot:", lines[1]);
        Assert.Equal(2 + FailureLogReport.MaxEntries + 1, lines.Length);
        Assert.Equal($"  Error [mymod] error {FailureLogReport.MaxEntries}", lines[1 + FailureLogReport.MaxEntries]);
        Assert.Equal("  and 3 more, see the log", lines[^1]);
    }

    [Fact]
    public void Describe_Should_NotCountWarnings_When_ErrorsAndWarningsAreMixed()
    {
        string report = FailureLogReport.Describe(
            DataPath,
            [Entry(EnumLogType.Warning, "mymod", "w"), Entry(EnumLogType.Error, "mymod", "e")]);

        Assert.Contains("[Atlas] 1 error(s) logged by the engine since the boot:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_MarkTheEntriesSinceTheScenarioStarted_When_AStartIsGiven()
    {
        // Two errors from the boot (and an earlier scenario), one from the failing scenario.
        string report = FailureLogReport.Describe(
            DataPath,
            [
                Entry(EnumLogType.Error, "mymod", "boot error"),
                Entry(EnumLogType.Error, "mymod", "earlier scenario error"),
                Entry(EnumLogType.Error, "mymod", "this scenario error"),
            ],
            scenarioStart: 2);

        string[] lines = report.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.Equal(
            "[Atlas] 3 error(s) logged by the engine since the boot, 1 of them since this scenario started (marked *):",
            lines[1]);
        Assert.Equal("  Error [mymod] boot error", lines[2]);
        Assert.Equal("  Error [mymod] earlier scenario error", lines[3]);
        Assert.Equal("* Error [mymod] this scenario error", lines[4]);
    }

    [Fact]
    public void Describe_Should_SayNoneWasLoggedSinceTheScenarioStarted_When_EveryErrorIsOlder()
    {
        string report = FailureLogReport.Describe(
            DataPath,
            [Entry(EnumLogType.Error, "mymod", "boot error")],
            scenarioStart: 1);

        string[] lines = report.Split('\n');
        Assert.Equal(
            "[Atlas] 1 error(s) logged by the engine since the boot, none of them since this scenario started:",
            lines[1]);
        Assert.Equal("  Error [mymod] boot error", lines[2]);
        Assert.DoesNotContain('*', report);
    }

    [Fact]
    public void Describe_Should_CountTheStartInTheWholeLog_When_WarningsComeBeforeIt()
    {
        // The start is a position in the boot diagnostics, which hold warnings too.
        string report = FailureLogReport.Describe(
            DataPath,
            [
                Entry(EnumLogType.Warning, "mymod", "w1"),
                Entry(EnumLogType.Error, "mymod", "old"),
                Entry(EnumLogType.Warning, "mymod", "w2"),
                Entry(EnumLogType.Error, "mymod", "new"),
            ],
            scenarioStart: 2);

        string[] lines = report.Split('\n');
        Assert.Equal("  Error [mymod] old", lines[2]);
        Assert.Equal("* Error [mymod] new", lines[3]);
    }

    [Fact]
    public void Describe_Should_NotMarkAnything_When_NoStartIsGiven()
    {
        string report = FailureLogReport.Describe(DataPath, [Entry(EnumLogType.Error, "mymod", "e")]);

        Assert.DoesNotContain('*', report);
        Assert.DoesNotContain("since this scenario started", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_ListTheScenariosEntriesFirst_When_TheBoundCutsTheList()
    {
        // Eight older errors would fill the list by themselves and hide the one the failing
        // scenario logged, which is the one the report is for.
        List<BootDiagnosticEntry> entries =
            [.. Enumerable.Range(1, 8).Select(i => Entry(EnumLogType.Error, "mymod", $"old {i}"))];
        entries.Add(Entry(EnumLogType.Error, "mymod", "new"));

        string report = FailureLogReport.Describe(DataPath, entries, scenarioStart: 8);

        string[] lines = report.Split('\n');
        Assert.Equal(
            "[Atlas] 9 error(s) logged by the engine since the boot, 1 of them since this scenario started (marked *):",
            lines[1]);

        // Chronological order, the marked entry last, the older ones filling the rest from the front.
        Assert.Equal(
            ["  Error [mymod] old 1", "  Error [mymod] old 2", "  Error [mymod] old 3", "  Error [mymod] old 4", "* Error [mymod] new"],
            lines[2..7]);
        Assert.Equal("  and 4 more, see the log", lines[^1]);
    }

    [Fact]
    public void Describe_Should_ListOnlyTheScenariosFirstEntries_When_MoreThanTheBoundWereLoggedSinceItStarted()
    {
        IReadOnlyList<BootDiagnosticEntry> entries =
            [.. Enumerable.Range(1, FailureLogReport.MaxEntries + 2).Select(i => Entry(EnumLogType.Error, "mymod", $"error {i}"))];

        string report = FailureLogReport.Describe(DataPath, entries, scenarioStart: 0);

        string[] lines = report.Split('\n');
        Assert.Equal(2 + FailureLogReport.MaxEntries + 1, lines.Length);
        Assert.All(lines[2..^1], line => Assert.StartsWith("* Error", line, StringComparison.Ordinal));
        Assert.Equal("  and 2 more, see the log", lines[^1]);
    }

    private static BootDiagnosticEntry Entry(EnumLogType level, string source, string message)
        => new(level, source, message, null);
}
