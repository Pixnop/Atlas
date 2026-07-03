using Atlas.XUnit;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
[AtlasWorld(Seed = 777)]
public class AdapterTests : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Scenario_Should_RunOnGameThread_When_Invoked()
    {
        Assert.Equal(777, World.Api.World.Seed);
        await World.Ticks(2);
        Assert.NotNull(World.BlockAt(World.Spawn).Code);
    }

    [AtlasScenario(FreshWorld = true)]
    public async Task Scenario_Should_GetFreshWorld_When_OptedOut()
    {
        World.SetBlock("game:soil-medium-normal", World.Spawn.Offset(0, 1, 0));
        await World.Ticks(1);
        Assert.Equal(777, World.Api.World.Seed);
    }
}
