using System.Globalization;
using System.Text;

namespace Atlas.Cli;

/// <summary>Aggregates the outcomes of every worker into live console lines, per-class wall
/// clocks, the final summary (including the speedup versus running the classes back to back)
/// and the process exit code. Pure and thread-safe: worker loops feed it concurrently and the
/// shell prints whatever it returns, so every formatting and exit-code decision is
/// unit-testable without a process in sight.</summary>
internal sealed class ParallelRunReport
{
    private readonly object _sync = new();
    private readonly List<TestOutcome> _outcomes = [];
    private readonly List<ClassTiming> _classTimings = [];

    private int _passed;
    private int _failed;
    private int _skipped;

    /// <summary>Gets the process exit code for the run: 0 only when every scenario passed and at
    /// least one ran (an empty run is a failure, same rule as the sequential runner).</summary>
    public int ExitCode
    {
        get
        {
            lock (_sync)
            {
                return _failed > 0 || Total == 0 ? 1 : 0;
            }
        }
    }

    /// <summary>Gets a snapshot of every outcome recorded so far (the TRX writer's input).</summary>
    public IReadOnlyList<TestOutcome> Outcomes
    {
        get
        {
            lock (_sync)
            {
                return [.. _outcomes];
            }
        }
    }

    private int Total => _passed + _failed + _skipped;

    /// <summary>Records one scenario outcome (a worker event or a synthesized crash failure).</summary>
    /// <param name="outcome">The outcome to record.</param>
    /// <returns>The console line (or multi-line block, for failures) to print.</returns>
    public string RecordTest(TestOutcome outcome)
    {
        lock (_sync)
        {
            _outcomes.Add(outcome);
            switch (outcome.Kind)
            {
                case TestOutcomeKind.Passed:
                    _passed++;
                    return $"PASS [{ShortName(outcome.ClassName)}] {outcome.TestName} ({Seconds(outcome.DurationMs)})";
                case TestOutcomeKind.Failed:
                    _failed++;
                    return FailBlock(outcome);
                default:
                    _skipped++;
                    return $"SKIP [{ShortName(outcome.ClassName)}] {outcome.TestName}: {outcome.Message}";
            }
        }
    }

    /// <summary>Records that a class finished (its worker exited, one way or another).</summary>
    /// <param name="className">The finished class.</param>
    /// <param name="wallClockMs">Wall-clock time the class occupied its worker, in milliseconds.</param>
    /// <returns>The console line to print.</returns>
    public string RecordClass(string className, long wallClockMs)
    {
        lock (_sync)
        {
            _classTimings.Add(new ClassTiming(className, wallClockMs));
            return $"[{ShortName(className)}] class finished in {Seconds(wallClockMs)}";
        }
    }

    /// <summary>Formats the end-of-run summary: totals, per-class wall clocks, and the speedup
    /// versus the sum of class times (what a sequential run of the same classes would cost).</summary>
    /// <param name="wallClockMs">Wall-clock duration of the whole orchestrated run.</param>
    /// <returns>The summary lines to print.</returns>
    public IReadOnlyList<string> Summary(long wallClockMs)
    {
        lock (_sync)
        {
            if (Total == 0)
            {
                return ["No scenarios ran (nothing matched, check the assembly path and --filter)."];
            }

            List<string> lines =
            [
                $"Total: {Total}, Passed: {_passed}, Failed: {_failed}, Skipped: {_skipped} (wall clock {Seconds(wallClockMs)})",
                "Per-class wall clock:",
            ];
            foreach (ClassTiming timing in _classTimings.OrderBy(timing => timing.ClassName, StringComparer.Ordinal))
            {
                lines.Add($"  {timing.ClassName}: {Seconds(timing.WallClockMs)}");
            }

            long classTimeSum = _classTimings.Sum(timing => timing.WallClockMs);
            if (wallClockMs > 0)
            {
                string factor = (classTimeSum / (decimal)wallClockMs).ToString("0.00", CultureInfo.InvariantCulture);
                lines.Add($"Speedup: {factor}x ({Seconds(classTimeSum)} of class time in {Seconds(wallClockMs)} of wall clock)");
            }

            return lines;
        }
    }

    private static string FailBlock(TestOutcome outcome)
    {
        var block = new StringBuilder();
        block.Append("FAIL [").Append(ShortName(outcome.ClassName)).Append("] ")
            .Append(outcome.TestName).Append(" (").Append(Seconds(outcome.DurationMs)).AppendLine(")");
        block.Append(Indent(outcome.Message ?? "unknown failure"));
        if (!string.IsNullOrWhiteSpace(outcome.Stack))
        {
            block.AppendLine();
            block.Append(Indent(outcome.Stack));
        }

        return block.ToString();
    }

    private static string ShortName(string className)
    {
        int lastDot = className.LastIndexOf('.');
        return lastDot < 0 ? className : className[(lastDot + 1)..];
    }

    private static string Seconds(long milliseconds) =>
        (milliseconds / 1000m).ToString("0.00", CultureInfo.InvariantCulture) + " s";

    private static string Indent(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return "     " + string.Join(Environment.NewLine + "     ", lines);
    }

    /// <summary>How long one class occupied its worker.</summary>
    /// <param name="ClassName">The class.</param>
    /// <param name="WallClockMs">Wall-clock milliseconds from worker spawn to worker exit.</param>
    private sealed record ClassTiming(string ClassName, long WallClockMs);
}
