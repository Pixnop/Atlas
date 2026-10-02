using System.Linq;
using Atlas.Internal.Bootstrap;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins the chunk registration of the entities Atlas moves. The engine brings an
/// entity's chunk index (<c>Entity.InChunkIndex3d</c>) and its entry in the chunk's entity list
/// in line with its position only once a second (<c>ServerSystemEntitySimulation.UpdateEvery1000ms</c>),
/// so a method that moves an entity has to do it itself or hand the scenario a window of up to
/// a second in which everything the engine centres on that chunk (random-tick candidates, an
/// entity lookup in the chunk) still sees the old one. Each scenario asserts straight after the
/// await, never after a wait. The orderer fixes the sequence: the class player joins first, F
/// is the first rollback-enabled scenario (so its lazy capture happens with the player at the
/// position C left it) and G restores it.</summary>
[TestCaseOrderer("Atlas.TestSupport.AlphabeticalOrderer", "Atlas.Engine.Tests")]
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 828282)]
public class EntityChunkIndexTests : AtlasScenarioBase
{
    private static ITestPlayer? _player;
    private static BlockPos? _capturedPos;

    [AtlasScenario]
    public async Task A_JoinPlayer_Should_ReturnPlayerWhoseChunkIndexMatchesItsPosition_When_ScatteredAcrossSpawn()
    {
        // The join scatters each player up to 50 blocks around a spawn that sits on a chunk
        // corner, so several joins land on both sides of a border. The engine registers the
        // entity from its final position when it spawns it, which is what this pins.
        _player = await World.JoinPlayer("ChunkIdxMain");
        AssertIndexMatchesPosition(_player);

        foreach (string name in new[] { "ChunkIdxB", "ChunkIdxC", "ChunkIdxD" })
        {
            AssertIndexMatchesPosition(await World.JoinPlayer(name));
        }
    }

    [AtlasScenario]
    public async Task B_SpawnEntity_Should_RegisterTheEntityInTheChunkOfItsPosition_When_Spawned()
    {
        BlockPos pos = World.Spawn.Offset(40, 1, 40);
        Entity chicken = World.SpawnEntity("game:chicken-rooster", pos);

        Assert.Equal(IndexOf(chicken), chicken.InChunkIndex3d);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task C_TeleportTo_Should_CompleteWithTheChunkIndexOfTheNewPosition_When_TheTeleportCrossesAChunkBorder()
    {
        ITestPlayer player = _player!;

        // Alternating hops of two chunks in X and one in Z: each one leaves the chunk the entity
        // is registered in, and each one used to complete with the index still on the old chunk
        // for 20 to 30 ticks (measured, 1.22.3).
        for (int hop = 0; hop < 4; hop++)
        {
            int sign = hop % 2 == 0 ? 1 : -1;
            BlockPos from = player.Position;
            var destination = new BlockPos(
                (((from.X / 32) + (2 * sign)) * 32) + 16,
                ((from.Y / 32) * 32) + 16,
                (((from.Z / 32) + sign) * 32) + 16,
                0);

            await player.TeleportTo(destination);

            Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);
            Assert.Equal(destination.X / 32, player.Position.X / 32);

            // The chunk's own entity list has the player too, not only the index.
            Assert.Contains(World.EntitiesIn(destination.Area(4)), e => e.EntityId == player.Entity.EntityId);
        }
    }

    [AtlasScenario]
    public async Task D_TeleportTo_Should_LeaveTheChunkIndexAlone_When_TheTargetIsInTheSameChunk()
    {
        ITestPlayer player = _player!;
        long before = player.Entity.InChunkIndex3d;
        BlockPos at = player.Position;
        var sameChunk = new BlockPos(((at.X / 32) * 32) + 8, ((at.Y / 32) * 32) + 16, ((at.Z / 32) * 32) + 8, 0);

        await player.TeleportTo(sameChunk);

        Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);
        Assert.Equal(before, player.Entity.InChunkIndex3d);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task F_Scenario_Should_CaptureWithThePlayerInPlaceThenTeleportAway_When_FirstRollbackScenarioRuns()
    {
        _capturedPos = _player!.Position;
        BlockPos away = _capturedPos.Offset(96, 0, 64);

        await _player.TeleportTo(away);

        Assert.Equal(IndexOf(_player.Entity), _player.Entity.InChunkIndex3d);
        Assert.NotEqual(_capturedPos.X / 32, _player.Position.X / 32);
        await World.Ticks(1);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task G_RollbackWorld_Should_RegisterTheRestoredPlayerInItsChunk_When_ItWasTeleportedAcrossChunks()
    {
        // The restore puts the player back at its captured position and reloads the columns,
        // which discards the chunk objects the player was registered in: right after the
        // rollback the index pointed at the polluted scenario's chunk and the player was in no
        // chunk's entity list (measured, 1.22.3: 24 ticks until the engine caught up).
        ITestPlayer player = _player!;
        Assert.Equal(_capturedPos, player.Position);

        Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);
        Assert.Contains(World.EntitiesIn(player.Position.Area(4)), e => e.EntityId == player.Entity.EntityId);
        await World.Ticks(1);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task H_RollbackWorld_Should_RegisterThePlayerInItsChunk_When_NothingMovedItSinceTheCapture()
    {
        // No teleport since the capture, so the player's index already matches its position and
        // the engine's once-a-second pass would see nothing to do: what the reload discarded is
        // the chunk's entity list entry, which only the rollback itself can put back.
        ITestPlayer player = _player!;
        Assert.Equal(_capturedPos, player.Position);

        Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);
        Assert.Contains(World.EntitiesIn(player.Position.Area(4)), e => e.EntityId == player.Entity.EntityId);
        await World.Ticks(1);
    }

    private void AssertIndexMatchesPosition(ITestPlayer player)
        => Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);

    // The chunk the entity's server-side position is in, by the engine's own arithmetic: the one
    // the engine's once-a-second pass registers the entity in, once it gets round to it.
    private long IndexOf(Entity entity)
        => World.Api.World.ChunkProvider.ChunkIndex3D(EngineCompat.SidedPosOf(entity));
}
