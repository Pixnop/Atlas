using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class ParallelRunReportTests
{
    [Fact]
    public void RecordTest_Should_FormatShortClassAndDuration_When_ScenarioPasses()
    {
        var report = new ParallelRunReport();

        string line = report.RecordTest(Pass("Ns.ChestScenarios", "Ns.ChestScenarios.Chest_survives", 1234));

        Assert.Equal("PASS [ChestScenarios] Ns.ChestScenarios.Chest_survives (1.23 s)", line);
    }

    [Fact]
    public void RecordTest_Should_IndentMessageAndStack_When_ScenarioFails()
    {
        var report = new ParallelRunReport();

        string block = report.RecordTest(new TestOutcome(
            "Ns.Suite", "Ns.Suite.T", TestOutcomeKind.Failed, 500, "Boom: values differ", "at Ns.Suite.T()\nat Runner"));

        string[] lines = block.Split(Environment.NewLine);
        Assert.Equal("FAIL [Suite] Ns.Suite.T (0.50 s)", lines[0]);
        Assert.Equal("     Boom: values differ", lines[1]);
        Assert.Equal("     at Ns.Suite.T()", lines[2]);
        Assert.Equal("     at Runner", lines[3]);
    }

    [Fact]
    public void RecordTest_Should_OmitTheStackBlock_When_FailureHasNone()
    {
        var report = new ParallelRunReport();

        string block = report.RecordTest(new TestOutcome("Ns.Suite", "Ns.Suite.T", TestOutcomeKind.Failed, 100, "Boom"));

        Assert.Equal(2, block.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void RecordTest_Should_IncludeTheReason_When_ScenarioIsSkipped()
    {
        var report = new ParallelRunReport();

        string line = report.RecordTest(new TestOutcome("Ns.Suite", "Ns.Suite.T", TestOutcomeKind.Skipped, 0, "not today"));

        Assert.Equal("SKIP [Suite] Ns.Suite.T: not today", line);
    }

    [Fact]
    public void RecordClass_Should_FormatTheWallClock_When_ClassFinishes()
    {
        var report = new ParallelRunReport();

        Assert.Equal("[Suite] class finished in 2.50 s", report.RecordClass("Ns.Suite", 2500));
    }

    [Fact]
    public void Summary_Should_ListPerClassTimesAndSpeedup_When_RunIsMixed()
    {
        var report = new ParallelRunReport();
        report.RecordTest(Pass("Ns.B", "Ns.B.T1", 10));
        report.RecordTest(Pass("Ns.A", "Ns.A.T1", 10));
        report.RecordTest(new TestOutcome("Ns.A", "Ns.A.T2", TestOutcomeKind.Failed, 10, "Boom"));
        report.RecordClass("Ns.B", 2000);
        report.RecordClass("Ns.A", 4000);

        IReadOnlyList<string> lines = report.Summary(wallClockMs: 3000);

        Assert.Equal("Total: 3, Passed: 2, Failed: 1, Skipped: 0 (wall clock 3.00 s)", lines[0]);
        Assert.Equal("Per-class wall clock:", lines[1]);
        Assert.Equal("  Ns.A: 4.00 s", lines[2]);
        Assert.Equal("  Ns.B: 2.00 s", lines[3]);
        Assert.Equal("Speedup: 2.00x (6.00 s of class time in 3.00 s of wall clock)", lines[4]);
    }

    [Fact]
    public void Summary_Should_ExplainTheEmptyRun_When_NothingRan()
    {
        IReadOnlyList<string> lines = new ParallelRunReport().Summary(wallClockMs: 5);

        Assert.Single(lines);
        Assert.Contains("No scenarios ran", lines[0]);
    }

    [Fact]
    public void ExitCode_Should_BeZero_When_EverythingPassedOrSkipped()
    {
        var report = new ParallelRunReport();
        report.RecordTest(Pass("Ns.A", "Ns.A.T", 1));
        report.RecordTest(new TestOutcome("Ns.A", "Ns.A.S", TestOutcomeKind.Skipped, 0, "later"));

        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_AnyScenarioFailed()
    {
        var report = new ParallelRunReport();
        report.RecordTest(Pass("Ns.A", "Ns.A.T", 1));
        report.RecordTest(new TestOutcome("Ns.A", "Ns.A.F", TestOutcomeKind.Failed, 1, "Boom"));

        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_NothingRan()
    {
        Assert.Equal(1, new ParallelRunReport().ExitCode);
    }

    [Fact]
    public void Outcomes_Should_SnapshotEveryRecordedOutcome_When_Read()
    {
        var report = new ParallelRunReport();
        TestOutcome first = Pass("Ns.A", "Ns.A.T1", 1);
        TestOutcome second = new("Ns.B", "Ns.B.T1", TestOutcomeKind.Failed, 2, "Boom");
        report.RecordTest(first);
        report.RecordTest(second);

        Assert.Equal([first, second], report.Outcomes);
    }

    private static TestOutcome Pass(string className, string testName, long durationMs) =>
        new(className, testName, TestOutcomeKind.Passed, durationMs);
}
