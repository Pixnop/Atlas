using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Atlas.Cli;

/// <summary>Builds the aggregated TRX document for an orchestrated run: one UnitTestResult per
/// scenario, matching the schema VSTest emits closely enough for CI artifact upload and TRX
/// tooling (TestRun/Results/TestDefinitions/TestEntries/TestLists/ResultSummary, the standard
/// unit-test type and list ids). VSTest only writes TRX for tests it ran itself, so the
/// orchestrator serializes its own. Pure: ids derive deterministically from test names, and all
/// environmental facts come in through <see cref="TrxRunInfo"/>.</summary>
internal static class TrxReport
{
    /// <summary>VSTest's test type id for unit tests, on every UnitTestResult.</summary>
    private const string UnitTestType = "13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b";

    /// <summary>VSTest's standard "Results Not in a List" test list, which every result joins.</summary>
    private const string ResultsListId = "8c84fa94-04c1-424b-9868-57a2d4851a1d";

    /// <summary>VSTest's standard "All Loaded Results" test list, present for tool parity.</summary>
    private const string AllResultsListId = "19431567-8539-422a-85d7-44ee4e166bda";

    [SuppressMessage(
        "Minor Vulnerability",
        "S5332:Using clear-text protocols is security-sensitive",
        Justification = "This is the TRX schema's XML namespace identifier, mandated verbatim by the VSTest format; it names the schema and is never dereferenced over the network.")]
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>Builds the TRX document.</summary>
    /// <param name="run">Run-level metadata.</param>
    /// <param name="outcomes">Every aggregated scenario outcome.</param>
    /// <param name="runOutputLines">Run-level output lines (the per-class isolation summaries),
    /// serialized as the ResultSummary's StdOut, the schema's own run-level output slot (VSTest
    /// puts run-level messages there); empty adds nothing.</param>
    /// <returns>The TRX document, ready to save.</returns>
    public static XDocument Build(
        TrxRunInfo run, IReadOnlyList<TestOutcome> outcomes, IReadOnlyList<string>? runOutputLines = null)
    {
        var rows = outcomes.Select(outcome => new Row(outcome)).ToList();
        var testRun = new XElement(
            Ns + "TestRun",
            new XAttribute("id", DeterministicGuid($"run:{run.RunName}:{run.Started.UtcTicks}")),
            new XAttribute("name", run.RunName),
            new XAttribute("runUser", string.Empty),
            Times(run),
            new XElement(Ns + "Results", rows.Select(row => Result(row, run))),
            new XElement(Ns + "TestDefinitions", rows.Select(row => Definition(row, run))),
            new XElement(Ns + "TestEntries", rows.Select(Entry)),
            TestLists(),
            ResultSummary(outcomes, runOutputLines));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), testRun);
    }

    private static XElement Times(TrxRunInfo run) => new(
        Ns + "Times",
        new XAttribute("creation", Timestamp(run.Started)),
        new XAttribute("queuing", Timestamp(run.Started)),
        new XAttribute("start", Timestamp(run.Started)),
        new XAttribute("finish", Timestamp(run.Finished)));

    private static XElement Result(Row row, TrxRunInfo run)
    {
        var result = new XElement(
            Ns + "UnitTestResult",
            new XAttribute("executionId", row.ExecutionId),
            new XAttribute("testId", row.TestId),
            new XAttribute("testName", row.Outcome.TestName),
            new XAttribute("computerName", run.ComputerName),
            new XAttribute("duration", Duration(row.Outcome.DurationMs)),
            new XAttribute("startTime", Timestamp(run.Started)),
            new XAttribute("endTime", Timestamp(run.Finished)),
            new XAttribute("testType", UnitTestType),
            new XAttribute("outcome", OutcomeName(row.Outcome.Kind)),
            new XAttribute("testListId", ResultsListId),
            new XAttribute("relativeResultsDirectory", row.ExecutionId));
        if (row.Outcome.Kind == TestOutcomeKind.Failed)
        {
            result.Add(new XElement(
                Ns + "Output",
                new XElement(
                    Ns + "ErrorInfo",
                    new XElement(Ns + "Message", row.Outcome.Message ?? "unknown failure"),
                    row.Outcome.Stack is null ? null : new XElement(Ns + "StackTrace", row.Outcome.Stack))));
        }

        return result;
    }

    private static XElement Definition(Row row, TrxRunInfo run) => new(
        Ns + "UnitTest",
        new XAttribute("name", row.Outcome.TestName),
        new XAttribute("storage", run.AssemblyPath),
        new XAttribute("id", row.TestId),
        new XElement(Ns + "Execution", new XAttribute("id", row.ExecutionId)),
        new XElement(
            Ns + "TestMethod",
            new XAttribute("codeBase", run.AssemblyPath),
            new XAttribute("adapterTypeName", "executor://atlas-parallel-orchestrator/v1"),
            new XAttribute("className", row.Outcome.ClassName),
            new XAttribute("name", MethodName(row.Outcome))));

    private static XElement Entry(Row row) => new(
        Ns + "TestEntry",
        new XAttribute("testId", row.TestId),
        new XAttribute("executionId", row.ExecutionId),
        new XAttribute("testListId", ResultsListId));

    private static XElement TestLists() => new(
        Ns + "TestLists",
        new XElement(
            Ns + "TestList",
            new XAttribute("name", "Results Not in a List"),
            new XAttribute("id", ResultsListId)),
        new XElement(
            Ns + "TestList",
            new XAttribute("name", "All Loaded Results"),
            new XAttribute("id", AllResultsListId)));

    private static XElement ResultSummary(IReadOnlyList<TestOutcome> outcomes, IReadOnlyList<string>? runOutputLines)
    {
        int passed = outcomes.Count(outcome => outcome.Kind == TestOutcomeKind.Passed);
        int failed = outcomes.Count(outcome => outcome.Kind == TestOutcomeKind.Failed);
        int skipped = outcomes.Count(outcome => outcome.Kind == TestOutcomeKind.Skipped);
        return new XElement(
            Ns + "ResultSummary",
            new XAttribute("outcome", failed > 0 ? "Failed" : "Completed"),
            new XElement(
                Ns + "Counters",
                new XAttribute("total", outcomes.Count),
                new XAttribute("executed", passed + failed),
                new XAttribute("passed", passed),
                new XAttribute("failed", failed),
                new XAttribute("error", 0),
                new XAttribute("timeout", 0),
                new XAttribute("aborted", 0),
                new XAttribute("inconclusive", 0),
                new XAttribute("passedButRunAborted", 0),
                new XAttribute("notRunnable", 0),
                new XAttribute("notExecuted", skipped),
                new XAttribute("disconnected", 0),
                new XAttribute("warning", 0),
                new XAttribute("completed", 0),
                new XAttribute("inProgress", 0),
                new XAttribute("pending", 0)),
            RunOutput(runOutputLines));
    }

    /// <summary>Serializes the run-level output lines into the ResultSummary's Output/StdOut
    /// element (where VSTest itself puts run-level messages), or nothing when there are none:
    /// the summary lines are informational, so an empty run must not grow an empty element.</summary>
    private static XElement? RunOutput(IReadOnlyList<string>? lines) =>
        lines is null || lines.Count == 0
            ? null
            : new XElement(Ns + "Output", new XElement(Ns + "StdOut", string.Join(Environment.NewLine, lines)));

    private static string OutcomeName(TestOutcomeKind kind) => kind switch
    {
        TestOutcomeKind.Passed => "Passed",
        TestOutcomeKind.Failed => "Failed",
        _ => "NotExecuted",
    };

    private static string MethodName(TestOutcome outcome) =>
        outcome.TestName.StartsWith(outcome.ClassName + ".", StringComparison.Ordinal)
            ? outcome.TestName[(outcome.ClassName.Length + 1)..]
            : outcome.TestName;

    private static string Duration(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset moment) =>
        moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Deterministic id derivation, so the same run content always serializes to the
    /// same document (which makes the writer trivially testable).</summary>
    private static Guid DeterministicGuid(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>One outcome with its derived TRX identities.</summary>
    private sealed class Row(TestOutcome outcome)
    {
        /// <summary>Gets the outcome this row serializes.</summary>
        public TestOutcome Outcome { get; } = outcome;

        /// <summary>Gets the stable test definition id.</summary>
        public Guid TestId { get; } = DeterministicGuid($"test:{outcome.TestName}");

        /// <summary>Gets the stable execution id linking result, definition and entry.</summary>
        public Guid ExecutionId { get; } = DeterministicGuid($"execution:{outcome.TestName}");
    }
}
