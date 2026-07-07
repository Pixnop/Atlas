namespace Atlas.Cli;

/// <summary>One scenario result as the orchestrator aggregates it, whether reported by a worker
/// (test-pass/test-fail/test-skip events) or synthesized by crash translation.</summary>
/// <param name="ClassName">Fully qualified name of the scenario class.</param>
/// <param name="TestName">The scenario's display name (crash translation synthesizes one).</param>
/// <param name="Kind">How the scenario ended.</param>
/// <param name="DurationMs">Execution time in whole milliseconds (0 for skips and synthesized
/// failures).</param>
/// <param name="Message">Failure message or skip reason; null for passes.</param>
/// <param name="Stack">Failure stack trace, when one exists.</param>
internal sealed record TestOutcome(
    string ClassName,
    string TestName,
    TestOutcomeKind Kind,
    long DurationMs,
    string? Message = null,
    string? Stack = null);
