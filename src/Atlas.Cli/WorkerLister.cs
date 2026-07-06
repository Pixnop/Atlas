using System.Diagnostics;

namespace Atlas.Cli;

/// <summary>Implements `atlas run --list --worker`: emits one discovered protocol event per
/// scenario plus a closing run-end line, and boots nothing. The stage 2 orchestrator uses this
/// stream as its class inventory before partitioning work.</summary>
internal static class WorkerLister
{
    /// <summary>Streams the discovered scenarios as protocol events.</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter to apply.</param>
    /// <param name="output">Destination for the JSONL event stream (the worker's stdout).</param>
    /// <returns>The process exit code (0: listing itself cannot fail a build).</returns>
    public static int List(string assemblyPath, ScenarioFilter filter, TextWriter output)
    {
        var writer = new WorkerEventWriter(output);
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<DiscoveredScenario> scenarios = ScenarioDiscovery.Find(assemblyPath, filter);
        foreach (DiscoveredScenario scenario in scenarios)
        {
            writer.Write(new DiscoveredEvent { Class = scenario.ClassName, Test = scenario.DisplayName });
        }

        writer.Write(new RunEndEvent
        {
            Total = scenarios.Count,
            Passed = 0,
            Failed = 0,
            Skipped = 0,
            Errors = 0,
            WallClockMs = stopwatch.ElapsedMilliseconds,
            ExitCode = 0,
        });
        return 0;
    }
}
