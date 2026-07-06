using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Exercises the contradictory-isolation setup error end-to-end: a scenario asking for
/// both <c>FreshWorld</c> (recycle the whole host) and <c>RollbackWorld</c> (restore the existing
/// host's world snapshot) must fail with <c>AtlasSetupException</c> BEFORE any host is booted
/// (the resolver runs ahead of the registry, so this guinea pig class costs no server boot).</summary>
public class ConflictingIsolationScenarios : AtlasScenarioBase
{
    [AtlasScenario(FreshWorld = true, RollbackWorld = true)]
    public Task Scenario_Should_FailSetup_When_FreshWorldAndRollbackWorldAreCombined()
    {
        Assert.Fail("unreachable: the conflicting isolation attributes must fail the setup before the body runs");
        return Task.CompletedTask;
    }
}
