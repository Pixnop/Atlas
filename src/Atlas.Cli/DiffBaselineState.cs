namespace Atlas.Cli;

/// <summary>What the baseline knew about a test that fails in the candidate: the distinction
/// `atlas diff` reports next to every new failure.</summary>
internal enum DiffBaselineState
{
    /// <summary>The test passed in the baseline.</summary>
    Passed,

    /// <summary>The test was present but skipped (no verdict) in the baseline.</summary>
    Skipped,

    /// <summary>The test did not exist in the baseline.</summary>
    Absent,
}
