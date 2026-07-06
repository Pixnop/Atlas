using Xunit;
using Xunit.Abstractions;

namespace Atlas.Cli;

/// <summary>Implements `atlas run --list`: discovers the scenarios of a compiled test assembly
/// through the xunit front controller without executing anything, so no server boots and
/// VINTAGE_STORY is not required.</summary>
internal static class ScenarioLister
{
    /// <summary>Prints the discovered scenario display names, filtered and sorted.</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter to apply.</param>
    /// <param name="output">Destination for the listing.</param>
    /// <returns>The process exit code (0: listing itself cannot fail a build).</returns>
    public static int List(string assemblyPath, ScenarioFilter filter, TextWriter output)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        using var resolver = new ScenarioAssemblyResolver(Path.GetDirectoryName(fullPath)!);
        using var controller = new XunitFrontController(AppDomainSupport.Denied, fullPath, shadowCopy: false);
        using var sink = new DiscoverySink();

        controller.Find(includeSourceInformation: false, sink, TestFrameworkOptions.ForDiscovery());
        sink.WaitForCompletion();

        List<string> names = sink.DisplayNames
            .Where(filter.Matches)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (string name in names)
        {
            output.WriteLine(name);
        }

        output.WriteLine($"Discovered: {names.Count}");
        return 0;
    }

    /// <summary>Collects test case display names from xunit discovery messages and signals when
    /// discovery is complete.</summary>
    private sealed class DiscoverySink : IMessageSink, IDisposable
    {
        private readonly ManualResetEventSlim _finished = new();

        /// <summary>Gets the display names collected so far.</summary>
        public List<string> DisplayNames { get; } = [];

        /// <inheritdoc />
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage discovered)
            {
                DisplayNames.Add(discovered.TestCase.DisplayName);
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
