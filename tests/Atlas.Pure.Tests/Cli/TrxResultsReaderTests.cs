using System.Xml.Linq;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class TrxResultsReaderTests
{
    private const string Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [Fact]
    public void Read_Should_RoundTripEveryOutcome_When_ReadingWhatTrxReportWrote()
    {
        // The writer-reader round trip the diff depends on: whatever `atlas run --parallel
        // --trx` serializes must come back with the same identity, kind, duration and message.
        XDocument written = TrxReport.Build(
            new TrxRunInfo(
                "atlas run --parallel Scenarios.dll",
                "/tmp/Scenarios.dll",
                "box",
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 14, 12, 5, 0, TimeSpan.Zero)),
            [
                new TestOutcome("Ns.A", "Ns.A.Passes", TestOutcomeKind.Passed, 61500),
                new TestOutcome("Ns.A", "Ns.A.Fails", TestOutcomeKind.Failed, 20, "Boom: values differ", "at Ns.A.Fails()"),
                new TestOutcome("Ns.B", "Ns.B.Skips", TestOutcomeKind.Skipped, 0, "later"),
            ]);

        IReadOnlyList<TrxTestResult> results = TrxResultsReader.Read(written);

        Assert.Equal(3, results.Count);
        Assert.Equal(new TrxTestResult("Ns.A.Passes", TestOutcomeKind.Passed, 61500), results[0]);
        Assert.Equal(
            new TrxTestResult("Ns.A.Fails", TestOutcomeKind.Failed, 20, "Boom: values differ"), results[1]);
        Assert.Equal(new TrxTestResult("Ns.B.Skips", TestOutcomeKind.Skipped, 0), results[2]);
    }

    [Fact]
    public void Read_Should_KeepTheoryRowsDistinct_When_NamesCarryArguments()
    {
        XDocument written = TrxReport.Build(
            Info(),
            [
                new TestOutcome("Ns.A", "Ns.A.Theory(row: 1)", TestOutcomeKind.Passed, 5),
                new TestOutcome("Ns.A", "Ns.A.Theory(row: 2)", TestOutcomeKind.Failed, 6, "row 2 broke"),
            ]);

        IReadOnlyList<TrxTestResult> results = TrxResultsReader.Read(written);

        Assert.Equal(["Ns.A.Theory(row: 1)", "Ns.A.Theory(row: 2)"], results.Select(result => result.TestName));
        Assert.Equal([TestOutcomeKind.Passed, TestOutcomeKind.Failed], results.Select(result => result.Kind));
    }

    [Fact]
    public void Read_Should_TolerateVstestExtras_When_ReadingASpecConformingReport()
    {
        // The shape plain `dotnet test --logger trx` writes: TestSettings, local-offset
        // timestamps, 7-decimal durations, and elements the diff does not need.
        XDocument vstest = XDocument.Parse($"""
            <TestRun id="fad17867-5662-45ac-9e6e-3cba714416ee" name="user 2026-07-14" xmlns="{Ns}">
              <Times creation="2026-07-14T14:32:59.9604489+02:00" finish="2026-07-14T14:32:59.9806330+02:00" />
              <TestSettings name="default" id="e3a7f3fb-d429-40aa-8000-9ecb5dca6ca1">
                <Deployment runDeploymentRoot="user_2026-07-14" />
              </TestSettings>
              <Results>
                <UnitTestResult executionId="8f24f4c1-570d-4b8c-910c-cb7b032005ff"
                    testId="622203e9-6725-fd75-a3c1-f73360a45c28" testName="Ns.A.Passes"
                    computerName="box" duration="00:00:00.0029746"
                    startTime="2026-07-14T14:32:59.9197059+02:00" outcome="Passed"
                    testListId="8c84fa94-04c1-424b-9868-57a2d4851a1d" />
              </Results>
              <ResultSummary outcome="Completed" />
            </TestRun>
            """);

        IReadOnlyList<TrxTestResult> results = TrxResultsReader.Read(vstest);

        Assert.Equal([new TrxTestResult("Ns.A.Passes", TestOutcomeKind.Passed, 3)], results);
    }

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("PassedButRunAborted", "Passed")]
    [InlineData("passed", "Passed")]
    [InlineData("Failed", "Failed")]
    [InlineData("Error", "Failed")]
    [InlineData("Timeout", "Failed")]
    [InlineData("Aborted", "Failed")]
    [InlineData("Disconnected", "Failed")]
    [InlineData("NotExecuted", "Skipped")]
    [InlineData("Inconclusive", "Skipped")]
    [InlineData("Pending", "Skipped")]
    [InlineData("SomeFutureOutcome", "Skipped")]
    public void Read_Should_FoldTheSchemaOutcome_When_MappingToTheThreeKinds(string outcome, string expected)
    {
        // The expected kind travels as a string because TestOutcomeKind is internal to the CLI
        // and xunit theory signatures must stay public.
        XDocument trx = Document($"""<UnitTestResult testName="Ns.A.T" outcome="{outcome}" />""");

        Assert.Equal(Enum.Parse<TestOutcomeKind>(expected), TrxResultsReader.Read(trx)[0].Kind);
    }

    [Fact]
    public void Read_Should_TreatAMissingOutcomeAsSkipped_When_TheAttributeIsAbsent()
    {
        XDocument trx = Document("""<UnitTestResult testName="Ns.A.T" />""");

        Assert.Equal(TestOutcomeKind.Skipped, TrxResultsReader.Read(trx)[0].Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-duration")]
    public void Read_Should_ReturnANullDuration_When_TheDurationIsMissingOrUnparseable(string duration)
    {
        XDocument trx = Document(
            $"""<UnitTestResult testName="Ns.A.T" outcome="Passed" duration="{duration}" />""");

        Assert.Null(TrxResultsReader.Read(trx)[0].DurationMs);
    }

    [Fact]
    public void Read_Should_ReturnANullDuration_When_TheAttributeIsAbsent()
    {
        XDocument trx = Document("""<UnitTestResult testName="Ns.A.T" outcome="Passed" />""");

        Assert.Null(TrxResultsReader.Read(trx)[0].DurationMs);
    }

    [Fact]
    public void Read_Should_RoundTheDuration_When_ItCarriesSubMillisecondDigits()
    {
        XDocument trx = Document(
            """<UnitTestResult testName="Ns.A.T" outcome="Passed" duration="00:00:00.0029746" />""");

        Assert.Equal(3, TrxResultsReader.Read(trx)[0].DurationMs);
    }

    [Fact]
    public void Read_Should_DropTheResult_When_ItCarriesNoTestName()
    {
        // A result without a testName has no identity to diff by; the lenient reader drops it
        // instead of failing the whole comparison.
        XDocument trx = Document("""
            <UnitTestResult outcome="Passed" />
            <UnitTestResult testName="Ns.A.T" outcome="Passed" />
            """);

        Assert.Equal(["Ns.A.T"], TrxResultsReader.Read(trx).Select(result => result.TestName));
    }

    [Fact]
    public void Read_Should_ExtractStdOut_When_TheOutputElementCarriesOne()
    {
        XDocument trx = Document(
            """
            <UnitTestResult testName="Ns.A.T" outcome="Passed">
              <Output><StdOut>line one
            line two</StdOut></Output>
            </UnitTestResult>
            """);

        Assert.Equal("line one\nline two", TrxResultsReader.Read(trx)[0].StdOut);
    }

    [Fact]
    public void Read_Should_ReturnANullStdOut_When_TheOutputElementIsMissing()
    {
        XDocument trx = Document("""<UnitTestResult testName="Ns.A.T" outcome="Passed" />""");

        Assert.Null(TrxResultsReader.Read(trx)[0].StdOut);
    }

    [Fact]
    public void Read_Should_ReturnANullStdOut_When_TheOutputElementCarriesNoStdOut()
    {
        // Output exists (a failure's ErrorInfo, say) but without a StdOut child: still null, not
        // an empty string, since the element itself never appeared.
        XDocument trx = Document(
            """
            <UnitTestResult testName="Ns.A.T" outcome="Failed">
              <Output><ErrorInfo><Message>boom</Message></ErrorInfo></Output>
            </UnitTestResult>
            """);

        Assert.Null(TrxResultsReader.Read(trx)[0].StdOut);
    }

    [Fact]
    public void Read_Should_ReturnAnEmptyStdOut_When_TheStdOutElementIsPresentButEmpty()
    {
        // Present-but-empty is distinct from missing: the test ran and captured nothing, which
        // is still a real, reportable fact once --json-tests surfaces it.
        XDocument trx = Document(
            """<UnitTestResult testName="Ns.A.T" outcome="Passed"><Output><StdOut /></Output></UnitTestResult>""");

        Assert.Equal(string.Empty, TrxResultsReader.Read(trx)[0].StdOut);
    }

    [Fact]
    public void Read_Should_ReturnNoResults_When_TheRunIsEmpty()
    {
        Assert.Empty(TrxResultsReader.Read(Document(string.Empty)));
        Assert.Empty(TrxResultsReader.Read(XDocument.Parse($"""<TestRun xmlns="{Ns}" />""")));
    }

    [Fact]
    public void Read_Should_ReadTheReport_When_TheNamespaceIsMissing()
    {
        XDocument bare = XDocument.Parse(
            """<TestRun><Results><UnitTestResult testName="Ns.A.T" outcome="Failed" /></Results></TestRun>""");

        Assert.Equal([new TrxTestResult("Ns.A.T", TestOutcomeKind.Failed)], TrxResultsReader.Read(bare));
    }

    [Fact]
    public void Read_Should_Throw_When_TheRootIsNotATestRun()
    {
        var notTrx = XDocument.Parse("<html><body>404</body></html>");

        FormatException failure = Assert.Throws<FormatException>(() => TrxResultsReader.Read(notTrx));
        Assert.Contains("not a TRX report", failure.Message);
        Assert.Contains("html", failure.Message);
    }

    private static XDocument Document(string results) => XDocument.Parse(
        $"""<TestRun id="a" name="run" xmlns="{Ns}"><Results>{results}</Results></TestRun>""");

    private static TrxRunInfo Info() => new(
        "run",
        "/tmp/Scenarios.dll",
        "box",
        new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 14, 12, 1, 0, TimeSpan.Zero));
}
