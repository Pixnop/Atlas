namespace Atlas.Cli;

/// <summary>One test result as read back from a TRX report: the minimal facts the diff needs.
/// The test name is the identity (theory rows carry their arguments in the name, so each row is
/// its own test).</summary>
/// <param name="TestName">The result's testName attribute, the diff's identity key.</param>
/// <param name="Kind">The outcome, folded onto the CLI's three kinds (see
/// <see cref="TrxResultsReader"/> for the mapping).</param>
/// <param name="DurationMs">Execution time in whole milliseconds; null when the report carries
/// no parseable duration (the attribute is optional in the schema).</param>
/// <param name="Message">The failure message (Output/ErrorInfo/Message), when one exists.</param>
/// <param name="StdOut">The captured console output (Output/StdOut), when the element exists
/// (empty is a valid captured value, distinct from a missing element, which is null).</param>
internal sealed record TrxTestResult(
    string TestName,
    TestOutcomeKind Kind,
    long? DurationMs = null,
    string? Message = null,
    string? StdOut = null);
