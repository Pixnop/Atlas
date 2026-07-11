using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class RunReportTests
{
    [Fact]
    public void RecordPass_Should_FormatNameAndDuration_When_ScenarioPasses()
    {
        var report = new RunReport();

        string line = report.RecordPass("Suite.Chest_survives", 1.234m);

        Assert.Equal("PASS Suite.Chest_survives (1.23 s)", line);
    }

    [Fact]
    public void RecordPass_Should_IndentTestOutput_When_ScenarioReportsOutput()
    {
        var report = new RunReport();

        string block = report.RecordPass(
            "Suite.Chest_survives",
            1.234m,
            "[Atlas] world isolation degraded: RollbackWorld fell back to a full host recycle." + Environment.NewLine);

        string[] lines = block.Split(Environment.NewLine);
        Assert.Equal("PASS Suite.Chest_survives (1.23 s)", lines[0]);
        Assert.Equal(
            "     [Atlas] world isolation degraded: RollbackWorld fell back to a full host recycle.",
            lines[1]);
        Assert.Equal(2, lines.Length); // the trailing newline of the output is trimmed
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordPass_Should_StayOneLine_When_OutputIsAbsentOrBlank(string? output)
    {
        var report = new RunReport();

        Assert.Equal("PASS Suite.Test (0.10 s)", report.RecordPass("Suite.Test", 0.1m, output));
    }

    [Fact]
    public void RecordFail_Should_IndentTestOutputAfterException_When_ScenarioReportsOutput()
    {
        var report = new RunReport();

        string block = report.RecordFail(
            "Suite.Test", 0.5m, "Atlas.Api.AtlasIsolationException", "degraded", null, "[Atlas] details");

        string[] lines = block.Split(Environment.NewLine);
        Assert.Equal("FAIL Suite.Test (0.50 s)", lines[0]);
        Assert.Equal("     Atlas.Api.AtlasIsolationException: degraded", lines[1]);
        Assert.Equal("     [Atlas] details", lines[2]);
    }

    [Fact]
    public void RecordFail_Should_IndentExceptionAndStackTrace_When_ScenarioFails()
    {
        var report = new RunReport();

        string block = report.RecordFail(
            "Suite.Chest_survives", 0.5m, "Xunit.Sdk.EqualException", "values differ", "at Suite.Chest_survives()\nat Runner");

        string[] lines = block.Split(Environment.NewLine);
        Assert.Equal("FAIL Suite.Chest_survives (0.50 s)", lines[0]);
        Assert.Equal("     Xunit.Sdk.EqualException: values differ", lines[1]);
        Assert.Equal("     at Suite.Chest_survives()", lines[2]);
        Assert.Equal("     at Runner", lines[3]);
    }

    [Fact]
    public void RecordFail_Should_OmitStackTraceBlock_When_StackTraceIsMissing()
    {
        var report = new RunReport();

        string block = report.RecordFail("Suite.Test", 0.1m, "System.Exception", "boom", null);

        Assert.Equal(2, block.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void RecordSkip_Should_IncludeReason_When_ScenarioIsSkipped()
    {
        var report = new RunReport();

        Assert.Equal("SKIP Suite.Test: not today", report.RecordSkip("Suite.Test", "not today"));
    }

    [Fact]
    public void RecordError_Should_FormatTypeAndMessage_When_RunnerErrors()
    {
        var report = new RunReport();

        Assert.Equal("ERROR System.IO.IOException: disk gone", report.RecordError("System.IO.IOException", "disk gone"));
    }

    [Fact]
    public void Summary_Should_CountEveryOutcome_When_RunIsMixed()
    {
        var report = new RunReport();
        report.RecordPass("a", 1m);
        report.RecordPass("b", 1m);
        report.RecordFail("c", 1m, "T", "m", null);
        report.RecordSkip("d", "r");

        Assert.Equal("Total: 4, Passed: 2, Failed: 1, Skipped: 1 (3.00 s)", report.Summary(3m));
    }

    [Fact]
    public void Summary_Should_MentionRunnerErrors_When_AnyWereRecorded()
    {
        var report = new RunReport();
        report.RecordPass("a", 1m);
        report.RecordError("T", "m");

        Assert.Contains("1 runner error(s)", report.Summary(1m));
    }

    [Fact]
    public void Summary_Should_ExplainEmptyRun_When_NothingMatched()
    {
        var report = new RunReport();

        Assert.Contains("No scenarios ran", report.Summary(0m));
    }

    [Fact]
    public void ExitCode_Should_BeZero_When_EverythingPassed()
    {
        var report = new RunReport();
        report.RecordPass("a", 1m);
        report.RecordSkip("b", "r");

        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_AnyScenarioFailed()
    {
        var report = new RunReport();
        report.RecordPass("a", 1m);
        report.RecordFail("b", 1m, "T", "m", null);

        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_OnlyRunnerErrorsOccurred()
    {
        var report = new RunReport();
        report.RecordPass("a", 1m);
        report.RecordError("T", "m");

        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_NothingRan()
    {
        Assert.Equal(1, new RunReport().ExitCode);
    }
}
