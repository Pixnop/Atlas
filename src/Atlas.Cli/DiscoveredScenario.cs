namespace Atlas.Cli;

/// <summary>A scenario found by discovery: its class and its display name.</summary>
/// <param name="ClassName">Fully qualified name of the scenario class.</param>
/// <param name="DisplayName">The scenario's display name, as `dotnet test` would report it.</param>
internal sealed record DiscoveredScenario(string ClassName, string DisplayName);
