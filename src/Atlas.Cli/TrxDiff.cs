namespace Atlas.Cli;

/// <summary>Computes what changed between two runs: the pure core of `atlas diff`. Comparison is
/// keyed by test name exactly as the TRX reports it (theory rows carry their arguments in the
/// name, so every row diffs on its own). Duplicate names inside one report (rerun tooling writes
/// one result per attempt) are merged first, keeping the worst outcome, so a test that failed
/// any attempt counts as failed.</summary>
internal static class TrxDiff
{
    /// <summary>Computes the diff between the two runs.</summary>
    /// <param name="baseline">The baseline run's results.</param>
    /// <param name="candidate">The candidate run's results.</param>
    /// <returns>The categorized differences, each category sorted by test name.</returns>
    public static DiffResult Compute(IReadOnlyList<TrxTestResult> baseline, IReadOnlyList<TrxTestResult> candidate)
    {
        Dictionary<string, TrxTestResult> before = MergeDuplicates(baseline);
        Dictionary<string, TrxTestResult> after = MergeDuplicates(candidate);

        List<DiffFailure> newFailures = [];
        List<string> fixedTests = [];
        List<string> stillFailing = [];
        List<DiffNewTest> newTests = [];
        List<DurationShift> durationShifts = [];
        foreach (TrxTestResult test in Ordered(after.Values))
        {
            Categorize(test, before.GetValueOrDefault(test.TestName), newFailures, fixedTests, stillFailing, newTests);
            AddDurationShift(test, before.GetValueOrDefault(test.TestName), durationShifts);
        }

        List<DiffVanishedTest> vanished = Ordered(before.Values)
            .Where(test => !after.ContainsKey(test.TestName))
            .Select(test => new DiffVanishedTest(test.TestName, test.Kind))
            .ToList();
        return new DiffResult(
            before.Count, after.Count, newFailures, fixedTests, vanished, newTests, stillFailing, durationShifts);
    }

    /// <summary>Merges duplicate names on each side the same worst-outcome-first way
    /// <see cref="Compute"/> does, then pairs every distinct test name from either run: the
    /// opt-in per-test listing behind `--json-tests`. A test absent from a side is null on that
    /// side; the kept result's stdout (see <see cref="Merge"/>) travels with it.</summary>
    /// <param name="baseline">The baseline run's results.</param>
    /// <param name="candidate">The candidate run's results.</param>
    /// <returns>One entry per distinct test name across both runs, sorted by test name.</returns>
    public static IReadOnlyList<DiffTestEntry> MergeTests(
        IReadOnlyList<TrxTestResult> baseline, IReadOnlyList<TrxTestResult> candidate)
    {
        Dictionary<string, TrxTestResult> before = MergeDuplicates(baseline);
        Dictionary<string, TrxTestResult> after = MergeDuplicates(candidate);
        return before.Keys
            .Union(after.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new DiffTestEntry(name, before.GetValueOrDefault(name), after.GetValueOrDefault(name)))
            .ToList();
    }

    private static void Categorize(
        TrxTestResult test,
        TrxTestResult? baseline,
        List<DiffFailure> newFailures,
        List<string> fixedTests,
        List<string> stillFailing,
        List<DiffNewTest> newTests)
    {
        if (test.Kind == TestOutcomeKind.Failed)
        {
            if (baseline?.Kind == TestOutcomeKind.Failed)
            {
                stillFailing.Add(test.TestName);
            }
            else
            {
                newFailures.Add(new DiffFailure(test.TestName, BaselineStateOf(baseline), test.Message));
            }
        }
        else if (baseline is null)
        {
            newTests.Add(new DiffNewTest(test.TestName, test.Kind));
        }
        else if (baseline.Kind == TestOutcomeKind.Failed && test.Kind == TestOutcomeKind.Passed)
        {
            fixedTests.Add(test.TestName);
        }
    }

    /// <summary>Duration shifts are only measured between two PASSING runs of the test: a
    /// failure's duration measures where it broke, not how fast the test is.</summary>
    private static void AddDurationShift(TrxTestResult test, TrxTestResult? baseline, List<DurationShift> shifts)
    {
        if (test.Kind == TestOutcomeKind.Passed
            && baseline is { Kind: TestOutcomeKind.Passed }
            && DurationShiftRule.Evaluate(test.TestName, baseline.DurationMs, test.DurationMs) is { } shift)
        {
            shifts.Add(shift);
        }
    }

    private static DiffBaselineState BaselineStateOf(TrxTestResult? baseline) => baseline switch
    {
        null => DiffBaselineState.Absent,
        { Kind: TestOutcomeKind.Passed } => DiffBaselineState.Passed,
        _ => DiffBaselineState.Skipped,
    };

    private static Dictionary<string, TrxTestResult> MergeDuplicates(IReadOnlyList<TrxTestResult> results)
    {
        var merged = new Dictionary<string, TrxTestResult>(StringComparer.Ordinal);
        foreach (TrxTestResult result in results)
        {
            merged[result.TestName] = merged.TryGetValue(result.TestName, out TrxTestResult? existing)
                ? Merge(existing, result)
                : result;
        }

        return merged;
    }

    /// <summary>Merges two same-named results: the worst outcome wins (Failed over Passed over
    /// Skipped); among equals, the longer duration and the first available message are kept. The
    /// kept result's stdout survives as-is, with no fallback to the other attempt's stdout (unlike
    /// the message): whichever result wins the comparison (or is first, on a tie) is the one whose
    /// stdout is reported.</summary>
    private static TrxTestResult Merge(TrxTestResult first, TrxTestResult second)
    {
        if (Badness(first.Kind) != Badness(second.Kind))
        {
            return Badness(first.Kind) > Badness(second.Kind) ? first : second;
        }

        return first with
        {
            DurationMs = LongestOf(first.DurationMs, second.DurationMs),
            Message = first.Message ?? second.Message,
        };
    }

    private static int Badness(TestOutcomeKind kind) => kind switch
    {
        TestOutcomeKind.Failed => 2,
        TestOutcomeKind.Passed => 1,
        _ => 0,
    };

    private static long? LongestOf(long? first, long? second) =>
        first is null || second is null ? first ?? second : Math.Max(first.Value, second.Value);

    private static IEnumerable<TrxTestResult> Ordered(IEnumerable<TrxTestResult> results) =>
        results.OrderBy(result => result.TestName, StringComparer.Ordinal);
}
