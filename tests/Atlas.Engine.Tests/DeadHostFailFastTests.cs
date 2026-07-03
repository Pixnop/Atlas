using Atlas.Api;
using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Engine.Tests;

/// <summary>Covers the dead-host fail-fast path in its own class so marking the host dead cannot
/// starve a sibling test method: <see cref="HostRegistry"/> tracks dead classes per <see cref="Type"/>,
/// and xUnit does not guarantee reflection order for methods within a class, so this scenario is kept
/// alone and drives <see cref="HostRegistry"/> directly rather than relying on a second method running
/// afterward.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 910)]
public class DeadHostFailFastTests : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Scenario_Should_FailFast_When_ClassHostWasMarkedDead()
    {
        HostRegistry.MarkDead(typeof(DeadHostFailFastTests), "simulated crash for fail-fast coverage");

        var ex = await Assert.ThrowsAsync<ServerCrashedException>(
            () => HostRegistry.GetOrCreateAsync(typeof(DeadHostFailFastTests)));
        Assert.Contains("simulated crash for fail-fast coverage", ex.Message);
    }
}
