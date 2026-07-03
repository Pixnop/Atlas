using Atlas.Api;
using Atlas.XUnit;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
[AtlasWorld(Seed = 909)]
public class ErrorHandlingTests : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 3000)]
    public async Task Scenario_Should_FailWithTimeout_When_UntilNeverTrue()
    {
        var ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => World.Until(() => false, timeoutTicks: 10));
        Assert.Equal(10, ex.TicksWaited);
    }
}
