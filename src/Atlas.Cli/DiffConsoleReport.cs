using System.Globalization;

namespace Atlas.Cli;

/// <summary>Formats a <see cref="DiffResult"/> for humans: the two inputs, a one-line summary of
/// every category count, compact per-category listings (empty categories print nothing), and the
/// regression verdict. Plain text, like every other atlas output. Pure: the shell prints
/// whatever it returns.</summary>
internal static class DiffConsoleReport
{
    /// <summary>Formats the whole report.</summary>
    /// <param name="diff">The computed diff.</param>
    /// <param name="baselinePath">The baseline TRX path, as the user gave it.</param>
    /// <param name="candidatePath">The candidate TRX path, as the user gave it.</param>
    /// <returns>The console lines to print, in order.</returns>
    public static IReadOnlyList<string> Lines(DiffResult diff, string baselinePath, string candidatePath)
    {
        List<string> lines =
        [
            $"Baseline:  {baselinePath} ({diff.BaselineTotal} test(s))",
            $"Candidate: {candidatePath} ({diff.CandidateTotal} test(s))",
            string.Empty,
            Summary(diff),
        ];
        Append(lines, $"New failures ({diff.NewFailures.Count}):", diff.NewFailures.SelectMany(FailureLines));
        Append(lines, $"Fixed ({diff.FixedTests.Count}):", diff.FixedTests.Select(name => "  " + name));
        Append(
            lines,
            $"Vanished ({diff.VanishedTests.Count}):",
            diff.VanishedTests.Select(test => $"  {test.TestName} ({KindName(test.BaselineKind)} in baseline)"));
        Append(
            lines,
            $"New tests ({diff.NewTests.Count}):",
            diff.NewTests.Select(test => $"  {test.TestName} ({KindName(test.CandidateKind)})"));
        Append(lines, $"Still failing ({diff.StillFailing.Count}):", diff.StillFailing.Select(name => "  " + name));
        Append(
            lines,
            $"Duration shifts ({diff.DurationShifts.Count}):",
            diff.DurationShifts.Select(ShiftLine));
        lines.Add(string.Empty);
        lines.Add(Verdict(diff));
        return lines;
    }

    private static string Summary(DiffResult diff) =>
        $"Summary: {diff.NewFailures.Count} new failure(s), {diff.FixedTests.Count} fixed, "
        + $"{diff.VanishedTests.Count} vanished, {diff.NewTests.Count} new, "
        + $"{diff.StillFailing.Count} still failing, {diff.DurationShifts.Count} notable duration shift(s)";

    private static string Verdict(DiffResult diff) =>
        diff.HasRegressions
            ? "Regressions: " + (diff.NewFailures.Count + diff.VanishedTests.Count)
                + $" ({diff.NewFailures.Count} new failure(s), {diff.VanishedTests.Count} vanished test(s))."
            : "No regressions.";

    private static void Append(List<string> lines, string header, IEnumerable<string> items)
    {
        List<string> block = items.ToList();
        if (block.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(header);
            lines.AddRange(block);
        }
    }

    /// <summary>The failing test with what the baseline knew about it, plus the first line of
    /// the failure message indented under it (the full message and stack live in the TRX).</summary>
    private static IEnumerable<string> FailureLines(DiffFailure failure)
    {
        yield return $"  {failure.TestName} ({BaselineName(failure.Baseline)})";
        if (FirstLineOf(failure.Message) is { } message)
        {
            yield return "     " + message;
        }
    }

    private static string ShiftLine(DurationShift shift) =>
        $"  {shift.TestName}: {shift.BaselineMs} ms -> {shift.CandidateMs} ms "
        + $"({Factor(shift)}{(shift.Slower ? "slower" : "faster")})";

    /// <summary>The slower-over-faster ratio, one decimal; a 0 ms faster side prints no factor
    /// since the ratio is unbounded there.</summary>
    private static string Factor(DurationShift shift)
    {
        long slower = Math.Max(shift.BaselineMs, shift.CandidateMs);
        long faster = Math.Min(shift.BaselineMs, shift.CandidateMs);
        return faster == 0
            ? string.Empty
            : (slower / (decimal)faster).ToString("0.0", CultureInfo.InvariantCulture) + "x ";
    }

    private static string BaselineName(DiffBaselineState state) => state switch
    {
        DiffBaselineState.Passed => "passed in baseline",
        DiffBaselineState.Skipped => "skipped in baseline",
        _ => "not in baseline",
    };

    private static string KindName(TestOutcomeKind kind) => kind switch
    {
        TestOutcomeKind.Passed => "passed",
        TestOutcomeKind.Failed => "failed",
        _ => "skipped",
    };

    private static string? FirstLineOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        string trimmed = message.TrimStart('\r', '\n');
        int lineBreak = trimmed.IndexOfAny(['\r', '\n']);
        return lineBreak < 0 ? trimmed : trimmed[..lineBreak];
    }
}
