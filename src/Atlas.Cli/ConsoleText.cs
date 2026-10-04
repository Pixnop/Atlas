using System.Globalization;
using System.Text;

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
    /// produced from several worker loops at once never interleave. Every run line goes through
    /// here, and so does the text a scenario supplied (a failure message, a stack trace, captured
    /// output): its control characters are written as visible <c>\uXXXX</c> escapes, the ones
    /// <see cref="XmlOutput.Escape"/> writes in the TRX, so a payload printed raw cannot move the
    /// cursor or clear the screen. The console also escapes U+007F to U+009F, which XML 1.0 allows
    /// and the TRX keeps: that range holds the single-character CSI (U+009B) a terminal reads as
    /// an escape sequence. Tab, line feed and carriage return are left as they are.</summary>
    /// <param name="output">Destination writer.</param>
    /// <param name="line">The line or block to write.</param>
    public static void WriteLine(TextWriter output, string line)
    {
        string safe = EscapeDelAndC1(XmlOutput.Escape(line));
        lock (OutputLock)
        {
            output.WriteLine(safe);
        }
    }

    // The part of the control range XML 1.0 allows (so XmlOutput.Escape keeps it) that a terminal
    // still acts on: DEL and the C1 controls, CSI included.
    private static string EscapeDelAndC1(string text)
    {
        StringBuilder? escaped = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current is >= '\u007f' and <= '\u009f')
            {
                escaped ??= new StringBuilder(text.Length + 8).Append(text, 0, index);
                escaped.Append(CultureInfo.InvariantCulture, $"\\u{(int)current:X4}");
            }
            else
            {
                escaped?.Append(current);
            }
        }

        return escaped?.ToString() ?? text;
    }
}
