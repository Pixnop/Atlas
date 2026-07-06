using Atlas.XUnit;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers <c>[AtlasScenario(RollbackWorld = true)]</c> end-to-end through the real
/// xUnit adapter: scenario A is the class's first rollback-enabled scenario (so it triggers the
/// lazy snapshot capture, then pollutes the world), scenario B requests a rollback and must see
/// the snapshot state, not A's pollution. The orderer makes the A-then-B sequence deterministic;
/// the static fields carry A's evidence to B (xUnit news up the class per scenario).</summary>
[TestCaseOrderer("Atlas.Engine.Tests.AlphabeticalOrderer", "Atlas.Engine.Tests")]
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 636363)]
public class AdapterRollbackTests : AtlasScenarioBase
{
    private const string PolluteBlock = "game:rock-granite";
    private const string ModDataKey = "adapter-rollback";

    private static BlockPos? _pollutedPos;

    [AtlasScenario(RollbackWorld = true)]
    public async Task A_Scenario_Should_CaptureLazilyThenPollute_When_FirstRollbackScenarioRuns()
    {
        _pollutedPos = World.Spawn.Offset(0, 3, 0);
        Assert.Equal(0, World.BlockAt(_pollutedPos).BlockId); // air at capture time
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(ModDataKey));

        World.SetBlock(PolluteBlock, _pollutedPos);
        World.Api.WorldManager.SaveGame.StoreData(ModDataKey, new byte[] { 42 });
        await World.Ticks(2);

        Assert.NotEqual(0, World.BlockAt(_pollutedPos).BlockId); // the pollution really landed
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task B_Scenario_Should_SeeSnapshotWorld_When_RollbackWorldIsRequested()
    {
        Assert.NotNull(_pollutedPos); // the orderer ran A first

        await World.Ticks(1);
        Assert.Equal(0, World.BlockAt(_pollutedPos!).BlockId); // A's block is air again
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(ModDataKey)); // A's moddata is gone
    }
}
