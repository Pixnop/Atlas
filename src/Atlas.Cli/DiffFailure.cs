namespace Atlas.Cli;

/// <summary>One new failure found by `atlas diff`: a test that fails in the candidate without
/// having failed in the baseline.</summary>
/// <param name="TestName">The failing test.</param>
/// <param name="Baseline">What the baseline knew about the test.</param>
/// <param name="Message">The candidate's failure message, when the TRX carries one.</param>
internal sealed record DiffFailure(string TestName, DiffBaselineState Baseline, string? Message);
