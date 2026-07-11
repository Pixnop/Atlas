using System.Globalization;
using System.Text;

namespace Atlas.Cli;

/// <summary>Aggregates per-scenario results into console lines, counters and the process exit
/// code. Pure: the shell (<see cref="ScenarioRunner"/>) feeds it runner callbacks and writes the
/// returned lines, so every formatting and exit-code decision is unit-testable without a runner.</summary>
internal sealed class RunReport
{
    private int _passed;
    private int _failed;
    private int _skipped;
    private int _errors;

    /// <summary>Gets the process exit code for the run: 0 only when every scenario passed and at
    /// least one ran. An empty run is a failure too, so a typo'd `--filter` cannot go green in CI.</summary>
    public int ExitCode => _failed > 0 || _errors > 0 || Total == 0 ? 1 : 0;

    private int Total => _passed + _failed + _skipped;

    /// <summary>Records a passed scenario, with its test output, if any, indented under it (the
    /// channel Atlas's own isolation reports travel on, e.g. a degraded rollback).</summary>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="seconds">Execution time in seconds.</param>
    /// <param name="output">The test's aggregated output; null or whitespace prints nothing.</param>
    /// <returns>The console line (or block, when there is output) to print.</returns>
    public string RecordPass(string displayName, decimal seconds, string? output = null)
    {
        _passed++;
        var block = new StringBuilder();
        block.Append("PASS ").Append(displayName).Append(" (").Append(FormatDuration(seconds)).Append(')');
        AppendOutput(block, output);
        return block.ToString();
    }

    /// <summary>Records a failed scenario, with the exception details, and its test output, if
    /// any, indented under it.</summary>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="seconds">Execution time in seconds.</param>
    /// <param name="exceptionType">The failing exception's type name.</param>
    /// <param name="exceptionMessage">The failing exception's message.</param>
    /// <param name="stackTrace">The failing exception's stack trace, if any.</param>
    /// <param name="output">The test's aggregated output; null or whitespace prints nothing.</param>
    /// <returns>The (multi-line) console block to print.</returns>
    public string RecordFail(
        string displayName,
        decimal seconds,
        string exceptionType,
        string exceptionMessage,
        string? stackTrace,
        string? output = null)
    {
        _failed++;
        var block = new StringBuilder();
        block.Append("FAIL ").Append(displayName).Append(" (").Append(FormatDuration(seconds)).AppendLine(")");
        block.Append(Indent($"{exceptionType}: {exceptionMessage}"));
        if (!string.IsNullOrWhiteSpace(stackTrace))
        {
            block.AppendLine();
            block.Append(Indent(stackTrace));
        }

        AppendOutput(block, output);
        return block.ToString();
    }

    /// <summary>Records a skipped scenario.</summary>
    /// <param name="displayName">The scenario's display name.</param>
    /// <param name="reason">The skip reason.</param>
    /// <returns>The console line to print.</returns>
    public string RecordSkip(string displayName, string reason)
    {
        _skipped++;
        return $"SKIP {displayName}: {reason}";
    }

    /// <summary>Records a runner-level error (a failure outside any single scenario, such as a
    /// crashed fixture or an unhandled collection exception).</summary>
    /// <param name="exceptionType">The exception's type name.</param>
    /// <param name="exceptionMessage">The exception's message.</param>
    /// <returns>The console line to print.</returns>
    public string RecordError(string exceptionType, string exceptionMessage)
    {
        _errors++;
        return $"ERROR {exceptionType}: {exceptionMessage}";
    }

    /// <summary>Formats the end-of-run summary line.</summary>
    /// <param name="totalSeconds">Wall-clock execution time of the whole run, in seconds.</param>
    /// <returns>The summary line to print.</returns>
    public string Summary(decimal totalSeconds)
    {
        if (Total == 0)
        {
            return "No scenarios ran (nothing matched, check the assembly path and --filter).";
        }

        string errors = _errors > 0 ? $", {_errors} runner error(s)" : string.Empty;
        return $"Total: {Total}, Passed: {_passed}, Failed: {_failed}, Skipped: {_skipped}{errors} ({FormatDuration(totalSeconds)})";
    }

    private static string FormatDuration(decimal seconds) =>
        seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    private static void AppendOutput(StringBuilder block, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            block.AppendLine();
            block.Append(Indent(output.TrimEnd('\r', '\n')));
        }
    }

    private static string Indent(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return "     " + string.Join(Environment.NewLine + "     ", lines);
    }
}
