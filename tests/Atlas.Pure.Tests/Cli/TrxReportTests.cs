using System.Xml.Linq;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class TrxReportTests
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [Fact]
    public void Build_Should_EmitOneResultDefinitionAndEntryPerOutcome_When_RunIsMixed()
    {
        XDocument trx = TrxReport.Build(Info(), MixedOutcomes());

        Assert.Equal(Ns + "TestRun", trx.Root!.Name);
        Assert.Equal(3, trx.Root.Element(Ns + "Results")!.Elements(Ns + "UnitTestResult").Count());
        Assert.Equal(3, trx.Root.Element(Ns + "TestDefinitions")!.Elements(Ns + "UnitTest").Count());
        Assert.Equal(3, trx.Root.Element(Ns + "TestEntries")!.Elements(Ns + "TestEntry").Count());
    }

    [Fact]
    public void Build_Should_MapEveryOutcomeKind_When_Serializing()
    {
        XDocument trx = TrxReport.Build(Info(), MixedOutcomes());

        List<string> outcomes = trx.Root!.Element(Ns + "Results")!
            .Elements(Ns + "UnitTestResult")
            .Select(result => result.Attribute("outcome")!.Value)
            .ToList();
        Assert.Equal(["Passed", "Failed", "NotExecuted"], outcomes);
    }

    [Fact]
    public void Build_Should_CountOutcomesInTheSummary_When_RunIsMixed()
    {
        XDocument trx = TrxReport.Build(Info(), MixedOutcomes());

        XElement summary = trx.Root!.Element(Ns + "ResultSummary")!;
        Assert.Equal("Failed", summary.Attribute("outcome")!.Value);
        XElement counters = summary.Element(Ns + "Counters")!;
        Assert.Equal("3", counters.Attribute("total")!.Value);
        Assert.Equal("2", counters.Attribute("executed")!.Value);
        Assert.Equal("1", counters.Attribute("passed")!.Value);
        Assert.Equal("1", counters.Attribute("failed")!.Value);
        Assert.Equal("1", counters.Attribute("notExecuted")!.Value);
    }

    [Fact]
    public void Build_Should_MarkTheRunCompleted_When_NothingFailed()
    {
        XDocument trx = TrxReport.Build(Info(), [Pass("Ns.A", "Ns.A.T", 5)]);

        Assert.Equal("Completed", trx.Root!.Element(Ns + "ResultSummary")!.Attribute("outcome")!.Value);
    }

    [Fact]
    public void Build_Should_AttachRunLevelStdOut_When_OutputLinesAreGiven()
    {
        XDocument trx = TrxReport.Build(
            Info(),
            [Pass("Ns.A", "Ns.A.T", 5)],
            ["[Atlas] isolation summary for Ns.A: 1 restart(s) (7.1 s total).", "[Atlas] isolation summary for Ns.B: B."]);

        // The isolation summaries land in the schema's run-level output slot, where VSTest
        // itself puts run-level messages, so TRX tooling shows them without any schema bending.
        XElement stdOut = trx.Root!.Element(Ns + "ResultSummary")!.Element(Ns + "Output")!.Element(Ns + "StdOut")!;
        Assert.Contains("1 restart(s) (7.1 s total)", stdOut.Value);
        Assert.Contains("Ns.B: B.", stdOut.Value);
    }

    [Fact]
    public void Build_Should_OmitTheRunLevelOutput_When_ThereAreNoLines()
    {
        XDocument trx = TrxReport.Build(Info(), [Pass("Ns.A", "Ns.A.T", 5)], []);

        Assert.Null(trx.Root!.Element(Ns + "ResultSummary")!.Element(Ns + "Output"));
    }

    [Fact]
    public void Build_Should_AttachErrorInfo_When_ScenarioFailed()
    {
        XDocument trx = TrxReport.Build(
            Info(),
            [new TestOutcome("Ns.A", "Ns.A.T", TestOutcomeKind.Failed, 5, "Boom: values differ", "at Ns.A.T()")]);

        XElement errorInfo = trx.Root!.Element(Ns + "Results")!
            .Element(Ns + "UnitTestResult")!
            .Element(Ns + "Output")!
            .Element(Ns + "ErrorInfo")!;
        Assert.Equal("Boom: values differ", errorInfo.Element(Ns + "Message")!.Value);
        Assert.Equal("at Ns.A.T()", errorInfo.Element(Ns + "StackTrace")!.Value);
    }

    [Fact]
    public void Build_Should_OmitTheStackTraceElement_When_FailureHasNone()
    {
        XDocument trx = TrxReport.Build(
            Info(), [new TestOutcome("Ns.A", "Ns.A (worker crashed)", TestOutcomeKind.Failed, 0, "worker died")]);

        XElement errorInfo = trx.Root!.Element(Ns + "Results")!
            .Element(Ns + "UnitTestResult")!
            .Element(Ns + "Output")!
            .Element(Ns + "ErrorInfo")!;
        Assert.Equal("worker died", errorInfo.Element(Ns + "Message")!.Value);
        Assert.Null(errorInfo.Element(Ns + "StackTrace"));
    }

    [Fact]
    public void Build_Should_DeriveTheMethodName_When_TestNameExtendsTheClassName()
    {
        XDocument trx = TrxReport.Build(Info(), [Pass("Ns.A", "Ns.A.Chest_survives", 5)]);

        XElement method = trx.Root!.Element(Ns + "TestDefinitions")!
            .Element(Ns + "UnitTest")!
            .Element(Ns + "TestMethod")!;
        Assert.Equal("Chest_survives", method.Attribute("name")!.Value);
        Assert.Equal("Ns.A", method.Attribute("className")!.Value);
        Assert.Equal("/tmp/Scenarios.dll", method.Attribute("codeBase")!.Value);
    }

    [Fact]
    public void Build_Should_KeepTheFullTestName_When_NameDoesNotExtendTheClassName()
    {
        XDocument trx = TrxReport.Build(
            Info(), [new TestOutcome("Ns.A", "Ns.A (worker timed out)", TestOutcomeKind.Failed, 0, "killed")]);

        XElement method = trx.Root!.Element(Ns + "TestDefinitions")!
            .Element(Ns + "UnitTest")!
            .Element(Ns + "TestMethod")!;
        Assert.Equal("Ns.A (worker timed out)", method.Attribute("name")!.Value);
    }

    [Fact]
    public void Build_Should_LinkResultToDefinitionAndEntry_When_Serializing()
    {
        XDocument trx = TrxReport.Build(Info(), [Pass("Ns.A", "Ns.A.T", 5)]);

        XElement result = trx.Root!.Element(Ns + "Results")!.Element(Ns + "UnitTestResult")!;
        XElement definition = trx.Root.Element(Ns + "TestDefinitions")!.Element(Ns + "UnitTest")!;
        XElement entry = trx.Root.Element(Ns + "TestEntries")!.Element(Ns + "TestEntry")!;
        Assert.Equal(definition.Attribute("id")!.Value, result.Attribute("testId")!.Value);
        Assert.Equal(
            definition.Element(Ns + "Execution")!.Attribute("id")!.Value,
            result.Attribute("executionId")!.Value);
        Assert.Equal(result.Attribute("testId")!.Value, entry.Attribute("testId")!.Value);
        Assert.Equal(result.Attribute("executionId")!.Value, entry.Attribute("executionId")!.Value);
    }

    [Fact]
    public void Build_Should_FormatTheDuration_When_Serializing()
    {
        XDocument trx = TrxReport.Build(Info(), [Pass("Ns.A", "Ns.A.T", 61500)]);

        XElement result = trx.Root!.Element(Ns + "Results")!.Element(Ns + "UnitTestResult")!;
        Assert.Equal("00:01:01.5000000", result.Attribute("duration")!.Value);
    }

    [Fact]
    public void Build_Should_ProduceTheSameDocument_When_BuiltTwiceFromTheSameRun()
    {
        Assert.Equal(
            TrxReport.Build(Info(), MixedOutcomes()).ToString(),
            TrxReport.Build(Info(), MixedOutcomes()).ToString());
    }

    private static TrxRunInfo Info() => new(
        "atlas run --parallel Scenarios.dll",
        "/tmp/Scenarios.dll",
        "test-box",
        new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 6, 12, 5, 0, TimeSpan.Zero));

    private static List<TestOutcome> MixedOutcomes() =>
    [
        Pass("Ns.A", "Ns.A.T1", 10),
        new TestOutcome("Ns.A", "Ns.A.T2", TestOutcomeKind.Failed, 20, "Boom", "at Ns.A.T2()"),
        new TestOutcome("Ns.B", "Ns.B.T1", TestOutcomeKind.Skipped, 0, "later"),
    ];

    private static TestOutcome Pass(string className, string testName, long durationMs) =>
        new(className, testName, TestOutcomeKind.Passed, durationMs);
}
