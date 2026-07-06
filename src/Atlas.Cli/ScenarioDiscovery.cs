using Xunit;
using Xunit.Abstractions;

namespace Atlas.Cli;

/// <summary>Discovers the scenarios of a compiled test assembly through the xunit front
/// controller without executing anything: no server boots and VINTAGE_STORY is not required.
/// Shared by <see cref="ScenarioLister"/> (human listing) and <see cref="WorkerLister"/>
/// (protocol events).</summary>
internal static class ScenarioDiscovery
{
    /// <summary>Finds the scenarios of the assembly, filtered and sorted by class then display
    /// name (ordinal).</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter to apply.</param>
    /// <returns>The discovered scenarios.</returns>
    public static IReadOnlyList<DiscoveredScenario> Find(string assemblyPath, ScenarioFilter filter)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        using var resolver = new ScenarioAssemblyResolver(Path.GetDirectoryName(fullPath)!);
        using var controller = new XunitFrontController(AppDomainSupport.Denied, fullPath, shadowCopy: false);
        using var sink = new DiscoverySink();

        controller.Find(includeSourceInformation: false, sink, TestFrameworkOptions.ForDiscovery());
        sink.WaitForCompletion();

        return sink.Scenarios
            .Where(scenario => filter.Matches(scenario.DisplayName))
            .OrderBy(scenario => scenario.ClassName, StringComparer.Ordinal)
            .ThenBy(scenario => scenario.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Collects scenarios from xunit discovery messages and signals when discovery is
    /// complete.</summary>
    private sealed class DiscoverySink : IMessageSink, IDisposable
    {
        private readonly ManualResetEventSlim _finished = new();

        /// <summary>Gets the scenarios collected so far.</summary>
        public List<DiscoveredScenario> Scenarios { get; } = [];

        /// <inheritdoc />
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage discovered)
            {
                Scenarios.Add(new DiscoveredScenario(
                    discovered.TestCase.TestMethod.TestClass.Class.Name,
                    discovered.TestCase.DisplayName));
            }
            else if (message is IDiscoveryCompleteMessage)
            {
                _finished.Set();
            }

            return true;
        }

        /// <summary>Blocks until the discovery-complete message arrives.</summary>
        public void WaitForCompletion() => _finished.Wait();

        /// <inheritdoc />
        public void Dispose() => _finished.Dispose();
    }
}
