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
/// listed here, the others stay in <c>IWorldSession.BootDiagnostics</c> and the log. The list is
/// the class's, not the scenario's: a host serves every scenario of its class, so an error an
/// earlier scenario logged is in it too. A report given the position the failing scenario started
/// at marks the entries logged since, and lists them before the older ones when the bound cuts
/// the list.</remarks>
internal static class FailureLogReport
{
    /// <summary>The most entries the report lists before it only counts the rest.</summary>
    internal const int MaxEntries = 5;

    /// <summary>The longest an entry's line gets, indent and ellipsis included.</summary>
    internal const int MaxLineLength = 200;

    /// <summary>Gets where the engine writes its log under a host's scratch data path.</summary>
    /// <param name="dataPath">The host's scratch data path.</param>
    /// <returns>The full path of <c>Logs/server-main.log</c> under it.</returns>
    public static string LogPath(string dataPath) => Path.Combine(dataPath, "Logs", "server-main.log");

    /// <summary>Words the report.</summary>
    /// <param name="dataPath">The host's scratch data path, holding <c>Logs/server-main.log</c>.</param>
    /// <param name="entries">The host's boot diagnostics, oldest first.</param>
    /// <param name="scenarioStart">How many entries the diagnostics held when the failing scenario
    /// started, so the entries from that position on were logged since; <see langword="null"/> for
    /// a report about a boot, where no scenario ran and nothing is marked.</param>
    /// <returns>The line naming the server log, then, when the engine logged any Error or Fatal
    /// entry, a count line and the first <see cref="MaxEntries"/> of them (the ones logged since
    /// the scenario started first, each marked with <c>*</c>); lines are separated by <c>\n</c> and
    /// the text carries no trailing newline.</returns>
    public static string Describe(string dataPath, IReadOnlyList<BootDiagnosticEntry> entries, int? scenarioStart = null)
    {
        var report = new StringBuilder("[Atlas] server log: ").Append(LogPath(dataPath));
        List<(BootDiagnosticEntry Entry, bool Recent)> errors =
        [
            .. entries
                .Select((entry, position) => (Entry: entry, Recent: scenarioStart is { } start && position >= start))
                .Where(item => item.Entry.Level is EnumLogType.Error or EnumLogType.Fatal),
        ];
        if (errors.Count == 0)
        {
            return report.ToString();
        }

        int recent = errors.Count(item => item.Recent);
        report.Append("\n[Atlas] ").Append(errors.Count).Append(" error(s) logged by the engine since the boot");
        if (scenarioStart is not null)
        {
            report.Append(recent == 0
                ? ", none of them since this scenario started"
                : $", {recent} of them since this scenario started (marked *)");
        }

        report.Append(':');
        List<int> shown = Shown(errors);
        foreach (int index in shown)
        {
            report.Append('\n').Append(Line(errors[index].Entry, errors[index].Recent));
        }

        if (errors.Count > shown.Count)
        {
            report.Append("\n  and ").Append(errors.Count - shown.Count).Append(" more, see the log");
        }

        return report.ToString();
    }

    // The positions to list, oldest first: the entries logged since the scenario started take the
    // bound first, the older ones fill what is left from the front. Without a start nothing is
    // recent, so this is the first MaxEntries of the list.
    private static List<int> Shown(List<(BootDiagnosticEntry Entry, bool Recent)> errors)
    {
        List<int> shown = [.. Enumerable.Range(0, errors.Count).Where(index => errors[index].Recent).Take(MaxEntries)];
        shown.AddRange(Enumerable.Range(0, errors.Count).Where(index => !errors[index].Recent).Take(MaxEntries - shown.Count));
        shown.Sort();
        return shown;
    }

    private static string Line(BootDiagnosticEntry entry, bool recent)
    {
        string firstLine = entry.Message.Split('\n', 2)[0].TrimEnd('\r');
        string line = $"{(recent ? "* " : "  ")}{entry.Level} [{entry.DescribeSource()}] {firstLine}";
        return line.Length <= MaxLineLength ? line : string.Concat(line.AsSpan(0, MaxLineLength - 3), "...");
    }
}
