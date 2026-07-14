using System.Xml.Linq;

namespace Atlas.Cli;

/// <summary>Reads the test results out of a TRX document into <see cref="TrxTestResult"/>s: the
/// input side of `atlas diff`. Pure (the shell loads the file and hands the document in) and
/// deliberately lenient beyond the root check: it round-trips everything <see cref="TrxReport"/>
/// writes and tolerates any spec-conforming TRX (VSTest's own reports carry extra elements,
/// local-offset timestamps and richer outcome values), so it reads only what the diff needs and
/// ignores the rest.</summary>
internal static class TrxResultsReader
{
    /// <summary>Reads every UnitTestResult of the document.</summary>
    /// <param name="document">The loaded TRX document.</param>
    /// <returns>One result per UnitTestResult element carrying a testName, in document order
    /// (results without a testName have no identity to diff by and are dropped).</returns>
    /// <exception cref="FormatException">The document is not a TRX report (no TestRun root).</exception>
    public static IReadOnlyList<TrxTestResult> Read(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "TestRun")
        {
            throw new FormatException(
                $"not a TRX report (the root element is <{root?.Name.LocalName ?? "nothing"}>, expected <TestRun>)");
        }

        // Children are resolved in the root's own namespace: conforming TRX uses the TeamTest
        // 2010 namespace, but a report that dropped it still reads fine.
        XNamespace ns = root.Name.Namespace;
        return root.Elements(ns + "Results")
            .Elements(ns + "UnitTestResult")
            .Select(result => Parse(result, ns))
            .OfType<TrxTestResult>()
            .ToList();
    }

    private static TrxTestResult? Parse(XElement result, XNamespace ns)
    {
        string? testName = result.Attribute("testName")?.Value;
        return string.IsNullOrEmpty(testName)
            ? null
            : new TrxTestResult(
                testName,
                KindOf(result.Attribute("outcome")?.Value),
                DurationOf(result.Attribute("duration")?.Value),
                result.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "Message")?.Value);
    }

    /// <summary>Folds the schema's outcome values onto the CLI's three kinds: the values meaning
    /// "ran and succeeded" map to Passed, the values meaning "ran and did not succeed" map to
    /// Failed, and everything else (NotExecuted, Inconclusive, Pending, unknown future values, or
    /// no outcome at all) maps to Skipped, since those tests produced no verdict to diff.</summary>
    private static TestOutcomeKind KindOf(string? outcome)
    {
        if (Is(outcome, "Passed") || Is(outcome, "PassedButRunAborted"))
        {
            return TestOutcomeKind.Passed;
        }

        return Is(outcome, "Failed") || Is(outcome, "Error") || Is(outcome, "Timeout")
            || Is(outcome, "Aborted") || Is(outcome, "Disconnected")
            ? TestOutcomeKind.Failed
            : TestOutcomeKind.Skipped;
    }

    private static bool Is(string? outcome, string name) =>
        string.Equals(outcome, name, StringComparison.OrdinalIgnoreCase);

    private static long? DurationOf(string? duration) =>
        TimeSpan.TryParse(duration, System.Globalization.CultureInfo.InvariantCulture, out TimeSpan span)
            ? (long)Math.Round(span.TotalMilliseconds)
            : null;
}
