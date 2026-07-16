using System.Xml.Linq;
using Atlas.Cli;

namespace Atlas.Engine.Tests;

/// <summary>Drives the diff command's runner (`atlas diff`) in-process, the same way
/// <see cref="CliFacadeTests"/> drives the lister and runner: real files on disk, the real
/// reader, and the real exit-code decision. The two TRX inputs are written by
/// <see cref="TrxReport"/> itself, the exact writer `atlas run --parallel --trx` uses, from
/// outcomes shaped like two guinea-pig runs; producing them through a real double `--parallel`
/// run would boot two live servers (minutes of wall clock, and the guinea pigs deliberately
/// crash and wedge their workers) without covering one more line of the diff path, since the
/// command consumes exactly what the writer serializes. A hand-written VSTest-shaped report
/// keeps the `dotnet test` interoperability honest, and the failure paths (missing file, not
/// XML, not TRX) pin the exit-2 contract.</summary>
[Trait("Category", "E2E")]
public class DiffCommandTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-diffcmd-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Diff_Should_CategorizeEveryChangeAndExitOne_When_TheCandidateRegressed()
    {
        // Baseline: the guinea-pig suite as it used to look. Candidate: one pass broke (a
        // theory row, proving rows diff independently), one failure got fixed, one test
        // vanished, one appeared, one keeps failing, one pass slowed notably.
        string baseline = Write("baseline.trx", TrxReport.Build(
            Info("baseline"),
            [
                Pass("Atlas.GuineaPig.Scenarios.Rows.Theory_Should_Hold(row: 1)", 40),
                Pass("Atlas.GuineaPig.Scenarios.Rows.Theory_Should_Hold(row: 2)", 45),
                Fail("Atlas.GuineaPig.Scenarios.Legacy.Scenario_Should_GetFixed", "old bug"),
                Pass("Atlas.GuineaPig.Scenarios.Legacy.Scenario_Should_Vanish", 10),
                Fail("Atlas.GuineaPig.Scenarios.Legacy.Scenario_Should_KeepFailing", "known bug"),
                Pass("Atlas.GuineaPig.Scenarios.Slow.Scenario_Should_StayFast", 200),
            ]));
        string candidate = Write("candidate.trx", TrxReport.Build(
            Info("candidate"),
            [
                Pass("Atlas.GuineaPig.Scenarios.Rows.Theory_Should_Hold(row: 1)", 41),
                Fail("Atlas.GuineaPig.Scenarios.Rows.Theory_Should_Hold(row: 2)", "row 2 broke"),
                Pass("Atlas.GuineaPig.Scenarios.Legacy.Scenario_Should_GetFixed", 12),
                Fail("Atlas.GuineaPig.Scenarios.Legacy.Scenario_Should_KeepFailing", "known bug"),
                Pass("Atlas.GuineaPig.Scenarios.Slow.Scenario_Should_StayFast", 1400),
                Pass("Atlas.GuineaPig.Scenarios.Fresh.Scenario_Should_Appear", 5),
            ]));
        var output = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(baseline, candidate), output, output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Summary: 1 new failure(s), 1 fixed, 1 vanished, 1 new, 1 still failing, 1 notable duration shift(s)",
            text);
        Assert.Contains("Theory_Should_Hold(row: 2) (passed in baseline)", text);
        Assert.Contains("row 2 broke", text);
        Assert.Contains("Scenario_Should_GetFixed", text);
        Assert.Contains("Scenario_Should_Vanish (passed in baseline)", text);
        Assert.Contains("Scenario_Should_Appear (passed)", text);
        Assert.Contains("Scenario_Should_KeepFailing", text);
        Assert.Contains("Scenario_Should_StayFast: 200 ms -> 1400 ms (7.0x slower)", text);
        Assert.Contains("Regressions: 2 (1 new failure(s), 1 vanished test(s)).", text);
    }

    [Fact]
    public void Diff_Should_ReportNoRegressionsAndExitZero_When_TheSameReportIsDiffedAgainstItself()
    {
        string report = Write("run.trx", TrxReport.Build(
            Info("run"),
            [
                Pass("Ns.A.T1", 10),
                Fail("Ns.A.T2", "known bug"),
            ]));
        var output = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(report, report), output, output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("No regressions.", text);
        Assert.Contains("Still failing (1):", text);
    }

    [Fact]
    public void Diff_Should_EmitTheVersionedJson_When_JsonRequested()
    {
        string baseline = Write("b.trx", TrxReport.Build(Info("b"), [Pass("Ns.A.T", 10)]));
        string candidate = Write("c.trx", TrxReport.Build(Info("c"), [Fail("Ns.A.T", "broke")]));
        var output = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(baseline, candidate, Json: true), output, output);

        Assert.Equal(1, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        Assert.True(document.RootElement.GetProperty("regressions").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(
            "Ns.A.T", document.RootElement.GetProperty("newFailures")[0].GetProperty("test").GetString());
    }

    [Fact]
    public void Diff_Should_EmitThePerTestListing_When_JsonTestsRequested()
    {
        // JsonTests alone (no Json) proves --json-tests implies --json end to end. The
        // candidate is hand-written VSTest-shaped XML (TrxReport.Build, atlas's own writer,
        // does not emit per-test StdOut) so the candidate's Ns.A.T carries captured console
        // output the reader must surface; Ns.A.Only exists only in the baseline.
        string baseline = Write("b.trx", TrxReport.Build(
            Info("b"), [Pass("Ns.A.T", 10), Pass("Ns.A.Only", 7)]));
        const string candidateXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="fad17867-5662-45ac-9e6e-3cba714416ee" name="candidate"
                xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Ns.A.T" computerName="box" duration="00:00:00.0120000"
                    outcome="Failed">
                  <Output>
                    <StdOut>booting the guinea pig world...</StdOut>
                    <ErrorInfo><Message>broke</Message></ErrorInfo>
                  </Output>
                </UnitTestResult>
              </Results>
              <ResultSummary outcome="Failed" />
            </TestRun>
            """;
        string candidate = Path.Combine(_root.FullName, "c.trx");
        File.WriteAllText(candidate, candidateXml);
        var output = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(baseline, candidate, JsonTests: true), output, output);

        Assert.Equal(1, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        System.Text.Json.JsonElement tests = document.RootElement.GetProperty("tests");
        Assert.Equal(2, tests.GetArrayLength());

        System.Text.Json.JsonElement changed = tests[0].GetProperty("test").GetString() == "Ns.A.T" ? tests[0] : tests[1];
        Assert.Equal("Ns.A.T", changed.GetProperty("test").GetString());
        Assert.Equal("passed", changed.GetProperty("baseline").GetProperty("outcome").GetString());
        Assert.Equal(10, changed.GetProperty("baseline").GetProperty("durationMs").GetInt64());
        Assert.Equal("failed", changed.GetProperty("candidate").GetProperty("outcome").GetString());
        Assert.Equal(12, changed.GetProperty("candidate").GetProperty("durationMs").GetInt64());
        Assert.Equal("booting the guinea pig world...", changed.GetProperty("stdout").GetString());

        System.Text.Json.JsonElement onlyInBaseline = tests[0].GetProperty("test").GetString() == "Ns.A.Only" ? tests[0] : tests[1];
        Assert.Equal("Ns.A.Only", onlyInBaseline.GetProperty("test").GetString());
        Assert.Equal("passed", onlyInBaseline.GetProperty("baseline").GetProperty("outcome").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, onlyInBaseline.GetProperty("candidate").ValueKind);
        Assert.False(onlyInBaseline.TryGetProperty("stdout", out _));
    }

    [Fact]
    public void Diff_Should_ReadVstestShapedReports_When_TheBaselineComesFromDotnetTest()
    {
        // The shape plain `dotnet test --logger trx` writes: TestSettings, local offsets,
        // 7-decimal durations. One test passes there and fails in the atlas-written candidate.
        const string vstestReport = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="fad17867-5662-45ac-9e6e-3cba714416ee" name="user 2026-07-14"
                xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestSettings name="default" id="e3a7f3fb-d429-40aa-8000-9ecb5dca6ca1" />
              <Results>
                <UnitTestResult executionId="8f24f4c1-570d-4b8c-910c-cb7b032005ff"
                    testId="622203e9-6725-fd75-a3c1-f73360a45c28" testName="Ns.A.T"
                    computerName="box" duration="00:00:00.0029746"
                    startTime="2026-07-14T14:32:59.9197059+02:00" outcome="Passed"
                    testListId="8c84fa94-04c1-424b-9868-57a2d4851a1d" />
              </Results>
              <ResultSummary outcome="Completed" />
            </TestRun>
            """;
        string baseline = Path.Combine(_root.FullName, "vstest.trx");
        File.WriteAllText(baseline, vstestReport);
        string candidate = Write("candidate.trx", TrxReport.Build(Info("c"), [Fail("Ns.A.T", "broke")]));
        var output = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(baseline, candidate), output, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Ns.A.T (passed in baseline)", output.ToString());
    }

    [Fact]
    public void Diff_Should_ExitTwo_When_AFileIsMissing()
    {
        string candidate = Write("c.trx", TrxReport.Build(Info("c"), [Pass("Ns.A.T", 10)]));
        var error = new StringWriter();

        int exitCode = DiffRunner.Run(
            new DiffArguments(Path.Combine(_root.FullName, "nope.trx"), candidate), new StringWriter(), error);

        Assert.Equal(2, exitCode);
        Assert.Contains("cannot read the baseline TRX", error.ToString());
    }

    [Fact]
    public void Diff_Should_ExitTwo_When_AFileIsNotXml()
    {
        string baseline = Write("b.trx", TrxReport.Build(Info("b"), [Pass("Ns.A.T", 10)]));
        string garbage = Path.Combine(_root.FullName, "garbage.trx");
        File.WriteAllText(garbage, "this is not xml at all");
        var error = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(baseline, garbage), new StringWriter(), error);

        Assert.Equal(2, exitCode);
        Assert.Contains("cannot read the candidate TRX", error.ToString());
    }

    [Fact]
    public void Diff_Should_ExitTwo_When_TheXmlIsNotATestRun()
    {
        string notTrx = Path.Combine(_root.FullName, "settings.xml");
        File.WriteAllText(notTrx, "<Settings><Option/></Settings>");
        var error = new StringWriter();

        int exitCode = DiffRunner.Run(new DiffArguments(notTrx, notTrx), new StringWriter(), error);

        Assert.Equal(2, exitCode);
        Assert.Contains("not a TRX report", error.ToString());
    }

    private string Write(string fileName, XDocument document)
    {
        string path = Path.Combine(_root.FullName, fileName);
        document.Save(path);
        return path;
    }

    private static TrxRunInfo Info(string runName) => new(
        $"atlas run --parallel Atlas.GuineaPig.Scenarios.dll ({runName})",
        "/tmp/Atlas.GuineaPig.Scenarios.dll",
        "test-box",
        new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 14, 12, 5, 0, TimeSpan.Zero));

    private static TestOutcome Pass(string testName, long durationMs) =>
        new(ClassOf(testName), testName, TestOutcomeKind.Passed, durationMs);

    private static TestOutcome Fail(string testName, string message) =>
        new(ClassOf(testName), testName, TestOutcomeKind.Failed, 15, message, "at " + testName);

    private static string ClassOf(string testName)
    {
        string method = testName.Split('(')[0];
        return method[..method.LastIndexOf('.')];
    }
}
