using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers <c>IWorldSession.SaveNow</c> through the real xUnit adapter. The first four
/// scenarios pin what one forced save does (handlers once, on the game thread, the savegame
/// blob in the database before any restart, autosave left on, the chat broadcast); the next two
/// run it before a <c>RestartWorld</c> and before a <c>RollbackWorld</c>, the two isolation
/// modes it has to coexist with. The orderer fixes the sequence, and the one scenario that
/// joins a player runs last, since a joined player blocks <c>RestartWorld</c>.</summary>
[TestCaseOrderer("Atlas.TestSupport.AlphabeticalOrderer", "Atlas.Engine.Tests")]
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 515151)]
public class SaveNowTests : AtlasScenarioBase
{
    private const string PersistKey = "savenow-persist";
    private const string RollbackKey = "savenow-rollback";
    private const string PolluteBlock = "game:rock-granite";
    private static readonly byte[] PersistPayload = [3, 1, 4];

    private static BlockPos? _pollutedPos;

    [AtlasScenario]
    public async Task A_SaveNow_Should_RunGameWorldSaveHandlersOnceOnTheGameThread_And_LeaveTheSaveIdle()
    {
        int gameThread = Environment.CurrentManagedThreadId;
        var handlerThreads = new List<int>();
        void OnSave() => handlerThreads.Add(Environment.CurrentManagedThreadId);

        World.Api.Event.GameWorldSave += OnSave;
        try
        {
            await World.SaveNow();
        }
        finally
        {
            World.Api.Event.GameWorldSave -= OnSave;
        }

        // Once, inline: the engine runs the handlers inside the command, so they have already run
        // when the task completes, and on the game thread (the scenario's own thread).
        Assert.Equal([gameThread], handlerThreads);

        // The off-thread half has settled too: the data is in the database, not queued for it.
        Assert.False(EngineProbes.OffThreadSaveRunning(World.Api));
    }

    [AtlasScenario]
    public async Task B_SaveNow_Should_WriteTheSaveGameBlobToTheDatabase_Before_AnyRestart()
    {
        Assert.Null(EngineProbes.PersistedSaveGameData(World.Api, PersistKey));

        // What a mod does on GameWorldSave: write its data into the SaveGame being saved.
        void OnSave() => World.Api.WorldManager.SaveGame.StoreData(PersistKey, PersistPayload);

        World.Api.Event.GameWorldSave += OnSave;
        try
        {
            await World.SaveNow();
        }
        finally
        {
            World.Api.Event.GameWorldSave -= OnSave;
        }

        // Read back from the database, not from the live SaveGame: the save path ran and
        // finished, with no restart needed to reach it.
        Assert.Equal(PersistPayload, EngineProbes.PersistedSaveGameData(World.Api, PersistKey));
    }

    [AtlasScenario]
    public async Task C_SaveNow_Should_LeaveAutosaveEnabled_When_Called()
    {
        // MagicNum.ServerAutoSave is process-wide, and a rollback capture earlier in the run
        // leaves it at 0, so the scenario sets its own known value and restores the old one.
        long before = MagicNum.ServerAutoSave;
        MagicNum.ServerAutoSave = 300;
        try
        {
            await World.SaveNow();
            Assert.Equal(300, MagicNum.ServerAutoSave);
        }
        finally
        {
            MagicNum.ServerAutoSave = before;
        }
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task D_SaveNow_Should_LeaveDataThatSurvivesARestart_When_RestartWorldFollows()
    {
        // B's data is on the replacement host: it was in the database from B's save on, and the
        // restart's own save kept it.
        Assert.Equal(PersistPayload, World.Api.WorldManager.SaveGame.GetData(PersistKey));

        // And the new host saves again without trouble.
        await World.SaveNow();
        Assert.Equal(PersistPayload, EngineProbes.PersistedSaveGameData(World.Api, PersistKey));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task E_SaveNow_Should_PersistPollution_When_CalledInsideARollbackScenario()
    {
        _pollutedPos = World.Spawn.Offset(0, 3, 0);
        Assert.Equal(0, World.BlockAt(_pollutedPos).BlockId);
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(RollbackKey));

        World.SetBlock(PolluteBlock, _pollutedPos);
        World.Api.WorldManager.SaveGame.StoreData(RollbackKey, [9]);

        await World.SaveNow();

        Assert.Equal([9], EngineProbes.PersistedSaveGameData(World.Api, RollbackKey));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task F_RollbackWorld_Should_RestoreTheCapturedWorld_When_ASaveNowRanInTheScenarioBefore()
    {
        Assert.NotNull(_pollutedPos); // the orderer ran E first
        await World.Ticks(1);

        // E polluted the world and then saved the pollution into the database; the rollback
        // restores the snapshot's blobs over it.
        Assert.Equal(0, World.BlockAt(_pollutedPos!).BlockId);
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(RollbackKey));

        // The rolled-back world saves normally, and the snapshot was not taken over by it.
        await World.SaveNow();
        Assert.Null(EngineProbes.PersistedSaveGameData(World.Api, RollbackKey));
    }

    [AtlasScenario]
    public async Task G_SaveNow_Should_BroadcastSavingNoticeInPlayerChat_And_ClearDropsIt()
    {
        ITestPlayer player = await World.JoinPlayer("SaveNowPlayer");
        player.Client.Clear();

        await World.SaveNow();

        Assert.Contains(player.Client.ChatLines(), line => line.Contains("Saving game world", StringComparison.Ordinal));

        player.Client.Clear();
        Assert.Empty(player.Client.ChatLines());
    }
}
