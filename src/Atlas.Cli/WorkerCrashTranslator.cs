using System.Globalization;

namespace Atlas.Cli;

/// <summary>Decides whether a finished worker needs a synthesized failure: a worker that dies,
/// wedges, or exits nonzero without a failing scenario explaining it must surface as a failed
/// class (with a stderr tail for forensics), never as a silently shorter test list. Pure.</summary>
internal static class WorkerCrashTranslator
{
    private const int StderrTailLines = 15;
    private const int StderrTailChars = 2000;

    /// <summary>Translates the end of a worker into a synthesized failure, when one is needed.</summary>
    /// <param name="className">The class the worker was assigned.</param>
    /// <param name="observation">What arrived on the worker's stdout.</param>
    /// <param name="exit">How the worker process ended.</param>
    /// <param name="timeoutSeconds">The outer per-worker timeout that was in force.</param>
    /// <returns>The failure to add to the report, or null when the worker's own events already
    /// tell the whole story.</returns>
    public static TestOutcome? Translate(
        string className, WorkerClassObservation observation, WorkerExit exit, int timeoutSeconds)
    {
        if (exit.TimedOut)
        {
            string deadline = timeoutSeconds.ToString(CultureInfo.InvariantCulture);
            return Fail(
                className,
                "worker timed out",
                $"Worker exceeded its {deadline} s timeout on this class and was killed.",
                exit.Stderr);
        }

        if (!observation.SawRunEnd)
        {
            return Fail(
                className,
                "worker crashed",
                $"Worker exited with code {Describe(exit.ExitCode)} without a well-formed run-end.",
                exit.Stderr);
        }

        if (exit.ExitCode is not 0 && !observation.SawFailure)
        {
            string reason = observation.Errors.Count > 0
                ? string.Join(Environment.NewLine, observation.Errors)
                : $"Worker exited with code {Describe(exit.ExitCode)} without reporting a failing scenario.";
            return Fail(className, "worker failed", reason, exit.Stderr);
        }

        return null;
    }

    private static TestOutcome Fail(string className, string label, string reason, string stderr) =>
        new(className, $"{className} ({label})", TestOutcomeKind.Failed, DurationMs: 0, WithStderrTail(reason, stderr));

    private static string Describe(int? exitCode) =>
        exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown (the process could not run)";

    private static string WithStderrTail(string reason, string stderr)
    {
        string tail = Tail(stderr);
        return tail.Length == 0
            ? reason
            : $"{reason}{Environment.NewLine}stderr tail:{Environment.NewLine}{tail}";
    }

    private static string Tail(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return string.Empty;
        }

        string[] lines = stderr
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string tail = string.Join(Environment.NewLine, lines.TakeLast(StderrTailLines));
        return tail.Length <= StderrTailChars ? tail : tail[^StderrTailChars..];
    }
}
