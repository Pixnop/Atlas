using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class TrxDiffTests
{
    [Fact]
    public void Compute_Should_ReportANewFailure_When_APassingTestFails()
    {
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.T")],
            [Failed("Ns.A.T", "Boom: values differ")]);

        Assert.Equal(
            [new DiffFailure("Ns.A.T", DiffBaselineState.Passed, "Boom: values differ")], diff.NewFailures);
        Assert.True(diff.HasRegressions);
        Assert.Equal(1, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportANewFailure_When_ASkippedTestFails()
    {
        DiffResult diff = TrxDiff.Compute([Skipped("Ns.A.T")], [Failed("Ns.A.T")]);

        Assert.Equal([new DiffFailure("Ns.A.T", DiffBaselineState.Skipped, null)], diff.NewFailures);
    }

    [Fact]
    public void Compute_Should_ReportANewFailure_When_ATestAbsentFromTheBaselineFails()
    {
        DiffResult diff = TrxDiff.Compute([], [Failed("Ns.A.T", "broke on arrival")]);

        Assert.Equal([new DiffFailure("Ns.A.T", DiffBaselineState.Absent, "broke on arrival")], diff.NewFailures);
        Assert.Empty(diff.NewTests);
        Assert.Equal(1, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportStillFailing_When_TheTestFailsInBothRuns()
    {
        DiffResult diff = TrxDiff.Compute([Failed("Ns.A.T", "old message")], [Failed("Ns.A.T", "new message")]);

        Assert.Equal(["Ns.A.T"], diff.StillFailing);
        Assert.Empty(diff.NewFailures);
        Assert.False(diff.HasRegressions);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportFixed_When_AFailingTestPasses()
    {
        DiffResult diff = TrxDiff.Compute([Failed("Ns.A.T")], [Passed("Ns.A.T")]);

        Assert.Equal(["Ns.A.T"], diff.FixedTests);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_NotReportFixed_When_AFailingTestIsMerelySkipped()
    {
        // A skip carries no verdict: a failure silenced by skipping is deliberately not
        // celebrated as fixed (and lands in no category at all).
        DiffResult diff = TrxDiff.Compute([Failed("Ns.A.T")], [Skipped("Ns.A.T")]);

        Assert.Empty(diff.FixedTests);
        Assert.Empty(diff.NewFailures);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportVanished_When_ABaselineTestIsAbsentFromTheCandidate()
    {
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.Gone"), Failed("Ns.B.Gone"), Passed("Ns.C.Stays")],
            [Passed("Ns.C.Stays")]);

        Assert.Equal(
            [
                new DiffVanishedTest("Ns.A.Gone", TestOutcomeKind.Passed),
                new DiffVanishedTest("Ns.B.Gone", TestOutcomeKind.Failed),
            ],
            diff.VanishedTests);
        Assert.True(diff.HasRegressions);
        Assert.Equal(1, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportNewTests_When_TheCandidateGrewNonFailingTests()
    {
        DiffResult diff = TrxDiff.Compute([], [Passed("Ns.A.T1"), Skipped("Ns.A.T2")]);

        Assert.Equal(
            [
                new DiffNewTest("Ns.A.T1", TestOutcomeKind.Passed),
                new DiffNewTest("Ns.A.T2", TestOutcomeKind.Skipped),
            ],
            diff.NewTests);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportADurationShift_When_BothRunsPassAndTheShiftIsNotable()
    {
        DiffResult diff = TrxDiff.Compute([Passed("Ns.A.T", 200)], [Passed("Ns.A.T", 1400)]);

        Assert.Equal([new DurationShift("Ns.A.T", 200, 1400, Slower: true)], diff.DurationShifts);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_NotMeasureDurations_When_EitherRunDidNotPass()
    {
        // A failure's duration measures where it broke, not how fast the test is.
        DiffResult diff = TrxDiff.Compute(
            [Failed("Ns.A.T") with { DurationMs = 10 }, Passed("Ns.B.T", 10)],
            [Passed("Ns.A.T", 5000), Failed("Ns.B.T") with { DurationMs = 5000 }]);

        Assert.Empty(diff.DurationShifts);
    }

    [Fact]
    public void Compute_Should_ReportNothing_When_TheSameRunIsDiffedAgainstItself()
    {
        List<TrxTestResult> run = [Passed("Ns.A.T1", 100), Failed("Ns.A.T2"), Skipped("Ns.A.T3")];

        DiffResult diff = TrxDiff.Compute(run, run);

        Assert.Empty(diff.NewFailures);
        Assert.Empty(diff.FixedTests);
        Assert.Empty(diff.VanishedTests);
        Assert.Empty(diff.NewTests);
        Assert.Empty(diff.DurationShifts);
        Assert.Equal(["Ns.A.T2"], diff.StillFailing);
        Assert.False(diff.HasRegressions);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportNothing_When_BothRunsAreEmpty()
    {
        DiffResult diff = TrxDiff.Compute([], []);

        Assert.Equal(0, diff.BaselineTotal);
        Assert.Equal(0, diff.CandidateTotal);
        Assert.False(diff.HasRegressions);
        Assert.Equal(0, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_ReportEverythingVanished_When_TheCandidateIsEmpty()
    {
        DiffResult diff = TrxDiff.Compute([Passed("Ns.A.T1"), Passed("Ns.A.T2")], []);

        Assert.Equal(2, diff.VanishedTests.Count);
        Assert.Equal(1, diff.ExitCode);
    }

    [Fact]
    public void Compute_Should_DiffTheoryRowsIndependently_When_OnlyOneRowChanges()
    {
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.Theory(row: 1)"), Passed("Ns.A.Theory(row: 2)")],
            [Passed("Ns.A.Theory(row: 1)"), Failed("Ns.A.Theory(row: 2)", "row 2 broke")]);

        Assert.Equal(
            [new DiffFailure("Ns.A.Theory(row: 2)", DiffBaselineState.Passed, "row 2 broke")], diff.NewFailures);
        Assert.Empty(diff.VanishedTests);
        Assert.Empty(diff.NewTests);
    }

    [Fact]
    public void Compute_Should_CountAFailedAttempt_When_DuplicateNamesMixOutcomes()
    {
        // Rerun tooling writes one result per attempt under the same name: any failed attempt
        // makes the test failed, whichever order the attempts appear in.
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.T")],
            [Failed("Ns.A.T", "first attempt"), Passed("Ns.A.T")]);

        Assert.Equal([new DiffFailure("Ns.A.T", DiffBaselineState.Passed, "first attempt")], diff.NewFailures);
        Assert.Equal(1, diff.CandidateTotal);
    }

    [Fact]
    public void Compute_Should_MergeDuplicates_When_TheBaselineRepeatsAName()
    {
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.T"), Failed("Ns.A.T")],
            [Failed("Ns.A.T")]);

        Assert.Equal(1, diff.BaselineTotal);
        Assert.Equal(["Ns.A.T"], diff.StillFailing);
        Assert.Empty(diff.NewFailures);
    }

    [Fact]
    public void Compute_Should_KeepTheLongestDuration_When_DuplicatesShareTheWorstOutcome()
    {
        // Two passing attempts at 100 and 900 ms: the merged 900 against a 100 ms baseline is
        // a notable shift, proving the merge kept the longer attempt.
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.A.T", 100)],
            [Passed("Ns.A.T", 900), Passed("Ns.A.T", 100)]);

        Assert.Equal([new DurationShift("Ns.A.T", 100, 900, Slower: true)], diff.DurationShifts);
    }

    [Fact]
    public void Compute_Should_SortEveryCategory_When_ResultsArriveUnordered()
    {
        DiffResult diff = TrxDiff.Compute(
            [Passed("Ns.B.T"), Passed("Ns.A.T"), Passed("Ns.Z.Gone"), Passed("Ns.C.Gone")],
            [Failed("Ns.B.T"), Failed("Ns.A.T")]);

        Assert.Equal(["Ns.A.T", "Ns.B.T"], diff.NewFailures.Select(failure => failure.TestName));
        Assert.Equal(["Ns.C.Gone", "Ns.Z.Gone"], diff.VanishedTests.Select(test => test.TestName));
    }

    private static TrxTestResult Passed(string name, long? durationMs = null) =>
        new(name, TestOutcomeKind.Passed, durationMs);

    private static TrxTestResult Failed(string name, string? message = null) =>
        new(name, TestOutcomeKind.Failed, 0, message);

    private static TrxTestResult Skipped(string name) => new(name, TestOutcomeKind.Skipped, 0);
}
