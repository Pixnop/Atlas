namespace Atlas.Cli;

/// <summary>Implements `atlas run --list`: prints the scenarios found by
/// <see cref="ScenarioDiscovery"/> for humans, without executing anything, so no server boots
/// and VINTAGE_STORY is not required.</summary>
internal static class ScenarioLister
{
    /// <summary>Prints the discovered scenario display names, filtered and sorted.</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter to apply.</param>
    /// <param name="output">Destination for the listing.</param>
    /// <returns>The process exit code (0: listing itself cannot fail a build).</returns>
    public static int List(string assemblyPath, ScenarioFilter filter, TextWriter output)
    {
        List<string> names = ScenarioDiscovery.Find(assemblyPath, filter)
            .Select(scenario => scenario.DisplayName)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (string name in names)
        {
            output.WriteLine(name);
        }

        output.WriteLine($"Discovered: {names.Count}");
        return 0;
    }
}
