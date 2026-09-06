namespace Atlas.XUnit.Internal;

/// <summary>Optional subscriber for the per-class isolation summary lines
/// <see cref="HostRegistry"/> prints to stderr at every host hand-off. Plain runs (`dotnet
/// test`, `atlas run`) leave it uninstalled and keep stderr as the only channel; the CLI's
/// worker mode installs a handler so each summary also becomes a `class-summary` protocol
/// event on stdout, which the parallel orchestrator aggregates. Like the fixture-harvest seam
/// on <see cref="HostRegistry"/>, <c>Atlas.Cli.IsolationSummaryHook</c> installs the handler
/// against the Atlas.XUnit copy the scenario assembly ships, compiling against this signature
/// (the CLI ships no harness copy of its own). Changing it is a breaking change for older
/// tools, which then report version skew and exit 2 instead of installing.</summary>
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
