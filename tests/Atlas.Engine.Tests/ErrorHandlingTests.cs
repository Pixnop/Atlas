using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
[AtlasWorld(Seed = 909)]
public class ErrorHandlingTests : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 3000)]
    public async Task Scenario_Should_FailWithTimeout_When_UntilNeverTrue()
    {
        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => World.Until(() => false, timeoutTicks: 10));
        Assert.Equal(10, ex.TicksWaited);
        Assert.Equal(
            "Until predicate still false after 10 ticks (timeoutTicks is 10; pass a larger value to wait longer)",
            ex.Message);
    }

    [AtlasScenario(TimeoutMs = 3000)]
    public async Task Scenario_Should_NameTheTimeoutTicksBound_When_WaitForPositionNeverArrives()
    {
        Entity hen = World.SpawnEntity("game:chicken-rooster", World.Spawn.Offset(3, 1, 0));

        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => World.WaitForPosition(hen, _ => false, timeoutTicks: 7));

        Assert.Equal(7, ex.TicksWaited);
        Assert.Contains("(timeoutTicks is 7; pass a larger value to wait longer)", ex.Message, StringComparison.Ordinal);
    }
}
