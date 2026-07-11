namespace Atlas.XUnit.Internal;

/// <summary>Optional subscriber for the per-class isolation summary lines
/// <see cref="HostRegistry"/> prints to stderr at every host hand-off. Plain runs (`dotnet
/// test`, `atlas run`) leave it uninstalled and keep stderr as the only channel; the CLI's
/// worker mode installs a handler so each summary also becomes a `class-summary` protocol
/// event on stdout, which the parallel orchestrator aggregates. Like the fixture-harvest seam
/// on <see cref="HostRegistry"/>, the CLI installs the handler BY NAME through reflection (it
/// deliberately references neither Atlas nor Atlas.XUnit), so the type name, method name and
/// signature are load-bearing; keep <c>Atlas.Cli.IsolationSummaryHook</c> in sync when
/// changing any of them.</summary>
internal static class IsolationSummarySink
{
    private static volatile Action<string, string>? _handler;

    /// <summary>Installs (or, with <see langword="null"/>, removes) the process-wide handler.</summary>
    /// <param name="handler">Receives the class's fully qualified name and the formatted
    /// summary line; <see langword="null"/> uninstalls.</param>
    public static void Install(Action<string, string>? handler) => _handler = handler;

    /// <summary>Publishes one summary to the installed handler, if any.</summary>
    /// <param name="className">The scenario class's fully qualified name.</param>
    /// <param name="summaryLine">The formatted summary line, identical to the stderr line.</param>
    public static void Publish(string className, string summaryLine)
    {
        try
        {
            _handler?.Invoke(className, summaryLine);
        }
        catch
        {
            // A reporting failure must never fail the scenario whose host request triggered
            // the hand-off; stderr already carries the line for forensics.
        }
    }
}
