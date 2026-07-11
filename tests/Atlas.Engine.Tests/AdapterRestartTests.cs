using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers <c>[AtlasScenario(RestartWorld = true)]</c> end-to-end through the real
/// xUnit adapter: scenario A mutates the shared world (a placed block and SaveGame moddata,
/// the persistence-real payload a mod would write for reload), scenario B requests a restart
/// and must see both survive onto a genuinely different server instance. The orderer makes the
/// A-then-B sequence deterministic; the static fields carry A's evidence to B (xUnit news up
/// the class per scenario).</summary>
[TestCaseOrderer("Atlas.Engine.Tests.AlphabeticalOrderer", "Atlas.Engine.Tests")]
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 737373)]
public class AdapterRestartTests : AtlasScenarioBase
{
    private const string PersistedBlock = "game:rock-granite";
    private const string ModDataKey = "adapter-restart";
    private static readonly byte[] ModDataPayload = [7, 11];

    private static BlockPos? _mutatedPos;
    private static ICoreServerAPI? _apiBeforeRestart;

    [AtlasScenario]
    public async Task A_Scenario_Should_MutateWorldAndStoreModData_When_PreparingTheRestart()
    {
        _apiBeforeRestart = World.Api;
        _mutatedPos = World.Spawn.Offset(0, 3, 0);
        Assert.Equal(0, World.BlockAt(_mutatedPos).BlockId); // air before the mutation

        World.SetBlock(PersistedBlock, _mutatedPos);
        World.Api.WorldManager.SaveGame.StoreData(ModDataKey, ModDataPayload);
        await World.Ticks(2);

        Assert.NotEqual(0, World.BlockAt(_mutatedPos).BlockId); // the mutation really landed
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task B_Scenario_Should_SeeCarriedWorldOnReplacementHost_When_RestartWorldIsRequested()
    {
        Assert.NotNull(_mutatedPos); // the orderer ran A first

        // The server genuinely restarted: this scenario runs against a different live API
        // instance, not the one A saw.
        Assert.NotSame(_apiBeforeRestart, World.Api);

        await World.Ticks(1);

        // The world carried over across the save/load round trip: A's block is still there...
        Assert.NotEqual(0, World.BlockAt(_mutatedPos!).BlockId);

        // ...and A's SaveGame moddata survived the real persist-and-reload, the exact contract
        // FreshWorld (world thrown away) and RollbackWorld (no restart) cannot exercise.
        byte[]? persisted = World.Api.WorldManager.SaveGame.GetData(ModDataKey);
        Assert.NotNull(persisted);
        Assert.Equal(ModDataPayload, persisted);
    }
}
