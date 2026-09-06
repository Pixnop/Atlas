using System.Globalization;

namespace Atlas.Cli;

/// <summary>The console conventions both `atlas run` reports share: how a duration is written, how
/// a block of detail is indented under its result line, what an empty run says, and the lock that
/// keeps concurrently produced lines from interleaving. One copy, so the sequential and parallel
/// runs cannot drift apart in wording or in indentation.</summary>
internal static class ConsoleText
{
    /// <summary>Summary line of a run in which nothing ran at all. Both runners treat that as a
    /// failure, so a typo'd `--filter` cannot go green in CI.</summary>
    public const string NoScenariosRan =
        "No scenarios ran (nothing matched, check the assembly path and --filter).";

    private const string IndentPrefix = "     ";

    private static readonly object OutputLock = new();

    /// <summary>Formats a duration already measured in seconds.</summary>
    /// <param name="seconds">The duration, in seconds.</param>
    /// <returns>The duration with two decimals and the unit, e.g. <c>1.50 s</c>.</returns>
    public static string Seconds(decimal seconds) =>
        seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    /// <summary>Formats a duration measured in milliseconds, in the same seconds unit.</summary>
    /// <param name="milliseconds">The duration, in milliseconds.</param>
    /// <returns>The duration with two decimals and the unit, e.g. <c>1.50 s</c>.</returns>
    public static string Seconds(long milliseconds) => Seconds(milliseconds / 1000m);

    /// <summary>Indents every line of a detail block under its result line, normalizing CRLF so a
    /// stack trace or captured output lines up whatever wrote it.</summary>
    /// <param name="text">The block to indent.</param>
    /// <returns>The indented block, joined with the platform newline.</returns>
    public static string Indent(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return IndentPrefix + string.Join(Environment.NewLine + IndentPrefix, lines);
    }

    /// <summary>Writes one line (or block) to the console under a process-wide lock, so lines
    /// produced from several worker loops at once never interleave.</summary>
    /// <param name="output">Destination writer.</param>
    /// <param name="line">The line or block to write.</param>
    public static void WriteLine(TextWriter output, string line)
    {
        lock (OutputLock)
        {
            output.WriteLine(line);
        }
    }
}
