using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

namespace Atlas.Fixture.Scenarios;

/// <summary>Builder guinea pig for `atlas fixture`: an ordinary [AtlasScenario] whose side
/// effect is building the world. It places a marker block near spawn; the fixture command's
/// E2E round trip (Atlas.Engine.Tests' FixtureCommandTests) then boots a fresh host from the
/// harvested save and asserts the marker survived. The marker's block code and offset are the
/// contract that test pins, so keep them in sync when changing either.</summary>
[AtlasWorld(Seed = 923)]
public class FixtureBuilderScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Builder_Should_PlaceMarkerNearSpawn_When_BuildingTheFixtureWorld()
    {
        BlockPos marker = World.Spawn.Offset(3, 1, 0);
        World.SetBlock("game:soil-medium-normal", marker);
        await World.Ticks(5);
        Assert.Equal("game:soil-medium-normal", World.BlockAt(marker).Code.ToString());
    }
}
