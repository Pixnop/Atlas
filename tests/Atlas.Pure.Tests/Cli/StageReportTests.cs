using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class StageReportTests
{
    [Fact]
    public void Line_Should_ReportStaged_InTheAtlasVoice()
    {
        var result = new StageFileResult("VintagestoryAPI.dll and VintagestoryAPI.pdb", StageFileState.Staged);

        string line = StageReport.Line(result);

        Assert.StartsWith("[Atlas] ", line);
        Assert.Contains("VintagestoryAPI.dll and VintagestoryAPI.pdb", line);
        Assert.Contains("staged", line);
    }

    [Fact]
    public void Line_Should_ReportAlreadyIdentical()
    {
        var result = new StageFileResult("Newtonsoft.Json.dll", StageFileState.AlreadyIdentical);

        string line = StageReport.Line(result);

        Assert.Contains("Newtonsoft.Json.dll", line);
        Assert.Contains("already matches", line);
        Assert.Contains("nothing to do", line);
    }

    [Fact]
    public void Line_Should_ReportNothingToStage()
    {
        var result = new StageFileResult("Newtonsoft.Json.dll", StageFileState.NothingToStage);

        string line = StageReport.Line(result);

        Assert.Contains("Newtonsoft.Json.dll", line);
        Assert.Contains("no local copy", line);
        Assert.Contains("nothing to do", line);
    }

    [Fact]
    public void Line_Should_PrintTheFailureMessageVerbatim_WithTheAtlasPrefix()
    {
        var result = new StageFileResult(
            "VintagestoryAPI.dll and VintagestoryAPI.pdb", StageFileState.Failed, "the core's exact message");

        string line = StageReport.Line(result);

        Assert.Equal("atlas: the core's exact message", line);
    }

    [Fact]
    public void ExitCode_Should_BeZero_When_NoResultFailed()
    {
        StageFileResult[] results =
        [
            new StageFileResult("a", StageFileState.Staged),
            new StageFileResult("b", StageFileState.AlreadyIdentical),
            new StageFileResult("c", StageFileState.NothingToStage),
        ];

        Assert.Equal(0, StageReport.ExitCode(results));
    }

    [Fact]
    public void ExitCode_Should_BeTwo_When_AnyResultFailed()
    {
        StageFileResult[] results =
        [
            new StageFileResult("a", StageFileState.Staged),
            new StageFileResult("b", StageFileState.Failed, "boom"),
        ];

        Assert.Equal(2, StageReport.ExitCode(results));
    }

    [Fact]
    public void ExitCode_Should_BeZero_When_ResultsAreEmpty()
    {
        Assert.Equal(0, StageReport.ExitCode([]));
    }
}
