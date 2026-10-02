using System.Text;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Diagnostics;

/// <summary>Pure formatter for the report a failing scenario carries about the engine's own log:
/// where the server log is, and the Error and Fatal entries the engine logged since the boot, so
/// a failure caused by something a mod logged at boot (a port it could not bind, an asset it
/// could not parse) points at its cause instead of only at the scenario's own symptom. Kept free
/// of the host and of xUnit so the wording and the bounds are testable without a boot (the
/// <see cref="Atlas.Internal.Hosting.EngineStopDetection"/> pattern).</summary>
/// <remarks>Bounded on purpose: at most <see cref="MaxEntries"/> entries, one line of at most
/// <see cref="MaxLineLength"/> characters each (the first line of the message, never its stack),
/// then a count of the rest. The entries come from the boot diagnostics, which record
/// Warning-and-above engine log entries from the start of the boot on; only Error and Fatal are
/// listed here, the others stay in <c>IWorldSession.BootDiagnostics</c> and the log.</remarks>
internal static class FailureLogReport
{
    /// <summary>The most entries the report lists before it only counts the rest.</summary>
    internal const int MaxEntries = 5;

    /// <summary>The longest an entry's line gets, indent and ellipsis included.</summary>
    internal const int MaxLineLength = 200;

    /// <summary>Words the report.</summary>
    /// <param name="dataPath">The host's scratch data path, holding <c>Logs/server-main.log</c>.</param>
    /// <param name="entries">The host's boot diagnostics, oldest first.</param>
    /// <returns>The line naming the server log, then, when the engine logged any Error or Fatal
    /// entry, a count line and the first <see cref="MaxEntries"/> of them; lines are separated by
    /// <c>\n</c> and the text carries no trailing newline.</returns>
    public static string Describe(string dataPath, IReadOnlyList<BootDiagnosticEntry> entries)
    {
        var report = new StringBuilder("[Atlas] server log: ")
            .Append(Path.Combine(dataPath, "Logs", "server-main.log"));
        List<BootDiagnosticEntry> errors = [.. entries.Where(entry => entry.Level is EnumLogType.Error or EnumLogType.Fatal)];
        if (errors.Count == 0)
        {
            return report.ToString();
        }

        report.Append("\n[Atlas] ").Append(errors.Count).Append(" error(s) logged by the engine since the boot:");
        foreach (BootDiagnosticEntry entry in errors.Take(MaxEntries))
        {
            report.Append('\n').Append(Line(entry));
        }

        if (errors.Count > MaxEntries)
        {
            report.Append("\n  and ").Append(errors.Count - MaxEntries).Append(" more, see the log");
        }

        return report.ToString();
    }

    private static string Line(BootDiagnosticEntry entry)
    {
        string firstLine = entry.Message.Split('\n', 2)[0].TrimEnd('\r');
        string line = $"  {entry.Level} [{entry.DescribeSource()}] {firstLine}";
        return line.Length <= MaxLineLength ? line : string.Concat(line.AsSpan(0, MaxLineLength - 3), "...");
    }
}
