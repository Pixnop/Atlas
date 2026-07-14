using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class DiffConsoleReportTests
{
    [Fact]
    public void Lines_Should_NameBothInputsWithTheirTotals_When_Formatting()
    {
        IReadOnlyList<string> lines = DiffConsoleReport.Lines(Mixed(), "base.trx", "cand.trx");

        Assert.Contains("Baseline:  base.trx (6 test(s))", lines);
        Assert.Contains("Candidate: cand.trx (7 test(s))", lines);
    }

    [Fact]
    public void Lines_Should_CountEveryCategoryInTheSummary_When_Formatting()
    {
        IReadOnlyList<string> lines = DiffConsoleReport.Lines(Mixed(), "base.trx", "cand.trx");

        Assert.Contains(
            "Summary: 1 new failure(s), 1 fixed, 1 vanished, 1 new, 1 still failing, 1 notable duration shift(s)",
            lines);
    }

    [Fact]
    public void Lines_Should_ListEveryCategory_When_AllArePopulated()
    {
        string text = string.Join('\n', DiffConsoleReport.Lines(Mixed(), "base.trx", "cand.trx"));

        Assert.Contains("New failures (1):", text);
        Assert.Contains("  Ns.A.Breaks (passed in baseline)", text);
        Assert.Contains("     Assert.Equal() Failure", text);
        Assert.Contains("Fixed (1):", text);
        Assert.Contains("  Ns.A.GetsFixed", text);
        Assert.Contains("Vanished (1):", text);
        Assert.Contains("  Ns.A.Vanishes (passed in baseline)", text);
        Assert.Contains("New tests (1):", text);
        Assert.Contains("  Ns.A.Appears (passed)", text);
        Assert.Contains("Still failing (1):", text);
        Assert.Contains("  Ns.A.KeepsFailing", text);
        Assert.Contains("Duration shifts (1):", text);
        Assert.Contains("  Ns.A.SlowsDown: 200 ms -> 1400 ms (7.0x slower)", text);
        Assert.Contains("Regressions: 2 (1 new failure(s), 1 vanished test(s)).", text);
    }

    [Fact]
    public void Lines_Should_KeepOnlyTheFirstMessageLine_When_TheFailureMessageIsMultiLine()
    {
        string text = string.Join('\n', DiffConsoleReport.Lines(Mixed(), "base.trx", "cand.trx"));

        Assert.DoesNotContain("second line", text);
    }

    [Fact]
    public void Lines_Should_SkipEmptyCategories_When_NothingChanged()
    {
        IReadOnlyList<string> lines = DiffConsoleReport.Lines(Empty(), "base.trx", "cand.trx");

        string text = string.Join('\n', lines);
        Assert.DoesNotContain("New failures", text);
        Assert.DoesNotContain("Fixed", text);
        Assert.DoesNotContain("Vanished", text);
        Assert.DoesNotContain("New tests", text);
        Assert.DoesNotContain("Still failing", text);
        Assert.DoesNotContain("Duration shifts", text);
        Assert.Contains("No regressions.", text);
    }

    [Fact]
    public void Lines_Should_OmitTheMessageLine_When_TheFailureCarriesNone()
    {
        var diff = new DiffResult(
            1, 1, [new DiffFailure("Ns.A.T", DiffBaselineState.Absent, null)], [], [], [], [], []);

        IReadOnlyList<string> lines = DiffConsoleReport.Lines(diff, "base.trx", "cand.trx");

        Assert.Contains("  Ns.A.T (not in baseline)", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("     ", StringComparison.Ordinal));
    }

    [Fact]
    public void Lines_Should_DescribeTheFasterDirection_When_TheCandidateSpedUp()
    {
        var diff = new DiffResult(
            1, 1, [], [], [], [], [], [new DurationShift("Ns.A.T", 2000, 600, Slower: false)]);

        string text = string.Join('\n', DiffConsoleReport.Lines(diff, "base.trx", "cand.trx"));

        Assert.Contains("  Ns.A.T: 2000 ms -> 600 ms (3.3x faster)", text);
    }

    [Fact]
    public void Lines_Should_OmitTheFactor_When_TheFasterSideIsZero()
    {
        var diff = new DiffResult(
            1, 1, [], [], [], [], [], [new DurationShift("Ns.A.T", 0, 900, Slower: true)]);

        string text = string.Join('\n', DiffConsoleReport.Lines(diff, "base.trx", "cand.trx"));

        Assert.Contains("  Ns.A.T: 0 ms -> 900 ms (slower)", text);
    }

    private static DiffResult Mixed() => new(
        6,
        7,
        [new DiffFailure("Ns.A.Breaks", DiffBaselineState.Passed, "Assert.Equal() Failure\nsecond line")],
        ["Ns.A.GetsFixed"],
        [new DiffVanishedTest("Ns.A.Vanishes", TestOutcomeKind.Passed)],
        [new DiffNewTest("Ns.A.Appears", TestOutcomeKind.Passed)],
        ["Ns.A.KeepsFailing"],
        [new DurationShift("Ns.A.SlowsDown", 200, 1400, Slower: true)]);

    private static DiffResult Empty() => new(3, 3, [], [], [], [], [], []);
}
