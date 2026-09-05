using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Shows <c>RollbackWorld = true</c>: both scenarios write the same block at the same
/// position and both assert it was air on entry, so either one would fail if the other's write
/// survived. They pass in any order without a test orderer because Atlas restores the world to
/// the snapshot it captured at the class's first rollback scenario, which is what makes order
/// independence a property of the harness rather than of the test names.</summary>
[Trait("Category", "E2E")]
public class IsolationScenarios : AtlasScenarioBase
{
    [AtlasScenario(RollbackWorld = true)]
    public async Task Rollback_Should_RestoreAir_When_TheFirstScenarioWroteGranite()
    {
        await PlaceGraniteOnCleanAir();
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Rollback_Should_RestoreAir_When_TheSecondScenarioWroteGranite()
    {
        await PlaceGraniteOnCleanAir();
    }

    private async Task PlaceGraniteOnCleanAir()
    {
        BlockPos pos = World.Spawn.Offset(0, 3, 0);
        Assert.Equal("game:air", World.BlockAt(pos).Code.ToString());

        World.SetBlock("game:rock-granite", pos);
        await World.Ticks(2);

        Assert.Equal("game:rock-granite", World.BlockAt(pos).Code.ToString());
    }
}
