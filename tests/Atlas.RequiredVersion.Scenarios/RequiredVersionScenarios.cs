using Atlas.XUnit;
using Xunit;

[AtlasWorld(Seed = 951)]
public class RequiredVersionScenarios : AtlasScenarioBase
{
    /// <summary>The compiled game version this assembly claims: no install reports it.</summary>
    public const string StampedVersion = "0.0.1-stale";

    [AtlasScenario]
    public Task Scenario_Should_NeverRun_When_TheInstallIsNotTheCompiledVersion()
    {
        // Passing is the failure here: the boot is refused before this body can run.
        return Task.CompletedTask;
    }
}
