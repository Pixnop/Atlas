namespace Atlas.Cli;

/// <summary>The computed difference between a baseline run and a candidate run: the categories
/// `atlas diff` reports, each sorted by test name, plus the exit-code decision. The categories
/// are disjoint; a test whose only change is a skip transition (a pass or failure becoming
/// skipped) lands in none of them, deliberately: skips carry no verdict either way.</summary>
/// <param name="BaselineTotal">Distinct tests in the baseline (duplicates merged).</param>
/// <param name="CandidateTotal">Distinct tests in the candidate (duplicates merged).</param>
/// <param name="NewFailures">Tests failing in the candidate that did not fail in the baseline
/// (they passed, were skipped, or did not exist).</param>
/// <param name="FixedTests">Tests that failed in the baseline and pass in the candidate.</param>
/// <param name="VanishedTests">Tests present in the baseline and absent from the candidate.</param>
/// <param name="NewTests">Tests absent from the baseline and not failing in the candidate.</param>
/// <param name="StillFailing">Tests failing in both runs.</param>
/// <param name="DurationShifts">Tests passing in both runs whose duration shifted notably
/// (see <see cref="DurationShiftRule"/>).</param>
internal sealed record DiffResult(
    int BaselineTotal,
    int CandidateTotal,
    IReadOnlyList<DiffFailure> NewFailures,
    IReadOnlyList<string> FixedTests,
    IReadOnlyList<DiffVanishedTest> VanishedTests,
    IReadOnlyList<DiffNewTest> NewTests,
    IReadOnlyList<string> StillFailing,
    IReadOnlyList<DurationShift> DurationShifts)
{
    /// <summary>Gets a value indicating whether the candidate regressed: a regression is a new
    /// failure or a vanished test, and nothing else (still-failing tests already failed the
    /// baseline, new and fixed tests are progress, duration shifts are informational).</summary>
    public bool HasRegressions => NewFailures.Count > 0 || VanishedTests.Count > 0;

    /// <summary>Gets the process exit code for the comparison: 0 when the candidate has no
    /// regressions, 1 when it has (usage and IO errors exit 2, decided by the shell).</summary>
    public int ExitCode => HasRegressions ? 1 : 0;
}
