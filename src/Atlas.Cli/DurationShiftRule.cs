namespace Atlas.Cli;

/// <summary>Decides when a duration change between two runs of the same test is worth reporting.
/// Duration noise is real (JIT, disk cache, CI neighbors), so the rule is deliberately
/// conservative and requires BOTH conditions: the slower run took at least
/// <see cref="MinimumFactor"/>x the faster one, AND the absolute difference is at least
/// <see cref="MinimumAbsoluteShiftMs"/> ms. Documented in
/// docs/specs/2026-07-14-diff-command.md.</summary>
internal static class DurationShiftRule
{
    /// <summary>Minimum absolute difference, in milliseconds, for a shift to be notable (a
    /// 2 ms test becoming 6 ms is a 3x shift nobody should be paged for).</summary>
    internal const long MinimumAbsoluteShiftMs = 500;

    /// <summary>Minimum ratio of the slower duration over the faster one (a 0 ms side
    /// satisfies the ratio trivially; the absolute floor is the real gate there).</summary>
    internal const int MinimumFactor = 2;

    /// <summary>Evaluates one test's durations across the two runs.</summary>
    /// <param name="testName">The test both runs executed.</param>
    /// <param name="baselineMs">Its baseline duration; null when the report carried none.</param>
    /// <param name="candidateMs">Its candidate duration; null when the report carried none.</param>
    /// <returns>The notable shift, or null when the change is absent, unmeasurable (either
    /// duration missing) or below the thresholds.</returns>
    public static DurationShift? Evaluate(string testName, long? baselineMs, long? candidateMs)
    {
        if (baselineMs is not { } baseline || candidateMs is not { } candidate)
        {
            return null;
        }

        long slower = Math.Max(baseline, candidate);
        long faster = Math.Min(baseline, candidate);
        bool notable = slower - faster >= MinimumAbsoluteShiftMs && slower >= faster * MinimumFactor;
        return notable ? new DurationShift(testName, baseline, candidate, Slower: candidate > baseline) : null;
    }
}
