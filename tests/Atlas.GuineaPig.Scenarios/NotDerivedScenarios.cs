using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Exercises the guard for <c>[AtlasScenario]</c> applied to a class that does not
/// derive from <c>AtlasScenarioBase</c>: the scenario must FAIL with
/// <c>AtlasSetupException</c> naming the required base class, instead of a null-reference
/// somewhere inside the invoker.</summary>
public class NotDerivedScenarios
{
    [AtlasScenario]
    public Task Scenario_Should_FailSetup_When_ClassDoesNotDeriveFromBase()
    {
        Assert.Fail("unreachable: the invoker must fail the setup with AtlasSetupException before the body runs, because this class does not derive from AtlasScenarioBase");
        return Task.CompletedTask;
    }
}
