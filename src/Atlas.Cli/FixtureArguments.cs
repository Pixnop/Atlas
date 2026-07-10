namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas fixture` invocation.</summary>
/// <param name="AssemblyPath">Path to the compiled scenario assembly holding the builder.</param>
/// <param name="Scenario">Display-name substring selecting the builder scenario; it must match
/// exactly one scenario (enforced at run time by <see cref="FixtureScenarioSelection"/>).</param>
/// <param name="OutPath">Where to write the harvested `.vcdbs` world save.</param>
/// <param name="Force">When true, overwrite an existing <paramref name="OutPath"/> file.</param>
internal sealed record FixtureArguments(
    string AssemblyPath,
    string Scenario,
    string OutPath,
    bool Force = false);
