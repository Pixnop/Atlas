using System.Reflection;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers <see cref="ITestPlayer.ChunkSends"/> end to end: the server's own record of the
/// chunks and map chunks it sent a joined test player, read on the engine's three supported
/// versions the way a mod's <c>IWorldManagerAPI.HasChunk</c> reads the chunk half of it.</summary>
/// <remarks>Every scenario compares against something the engine says on its own (<c>HasChunk</c>,
/// the forced-send queue), so none passes on two empty answers.</remarks>
[Trait("Category", "E2E")]
public class ChunkSendRecordTests
{
    private const int Bound = 600;
    private const int ChunkSize = 32;
    private const int DimensionStride = 1024;

    [Fact]
    public async Task WasSentChunk_Should_AgreeWithHasChunk_And_WasSentMapChunk_Should_FollowTheColumn_When_ThePlayerHasJoined()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Receiver");
            IChunkSendRecord sends = player.ChunkSends;
            (int cx, int cy, int cz) = ChunkOf(player.Position);
            await world.Until(() => sends.WasSentMapChunk(cx, cz) && sends.WasSentChunk(cx, cy, cz), Bound);

            IWorldManagerAPI manager = world.Api.WorldManager;
            int slices = manager.MapSizeY / ChunkSize;
            int sentSlices = 0;
            int sentColumns = 0;
            for (int dx = -8; dx <= 8; dx++)
            {
                for (int dz = -8; dz <= 8; dz++)
                {
                    bool any = false;
                    for (int y = 0; y < slices; y++)
                    {
                        bool sent = sends.WasSentChunk(cx + dx, y, cz + dz);
                        Assert.Equal(manager.HasChunk(cx + dx, y, cz + dz, player.Player), sent);
                        any |= sent;
                        sentSlices += sent ? 1 : 0;
                    }

                    // The ring queues a column's map chunk with the first slice it sends, in the
                    // same pass, and nothing here unloads or force-sends anything.
                    Assert.Equal(any, sends.WasSentMapChunk(cx + dx, cz + dz));
                    sentColumns += any ? 1 : 0;
                }
            }

            // Not two empty answers: the player's own column and some of its neighbours were sent,
            // and the 17 by 17 window is wider than the view distance reaches.
            Assert.True(sentSlices >= slices, $"expected at least the player's own column, got {sentSlices} slices");
            Assert.InRange(sentColumns, 1, (17 * 17) - 1);
            Assert.False(sends.WasSentMapChunk(cx + 40, cz));
            Assert.False(sends.WasSentChunk(cx + 40, cy, cz));
        });
    }

    [Fact]
    public async Task Queries_Should_AnswerFalse_Never_Throw_When_ThePositionIsOutsideTheWorld()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Outsider");
            IChunkSendRecord sends = player.ChunkSends;
            (int cx, int cy, int cz) = ChunkOf(player.Position);
            await world.Until(() => sends.WasSentChunk(cx, cy, cz), Bound);
            int columns = world.Api.WorldManager.MapSizeX / ChunkSize;

            // A negative x is a position the engine's index arithmetic folds onto another chunk:
            // x - columns on the next row down lands exactly on the player's own chunk, which was
            // sent. The record answers about the position asked, not about the one it folds onto.
            Assert.False(sends.WasSentChunk(cx - columns, cy, cz + 1));
            Assert.False(sends.WasSentMapChunk(cx - columns, cz + 1));

            Assert.False(sends.WasSentChunk(-1, cy, cz));
            Assert.False(sends.WasSentChunk(cx, -1, cz));
            Assert.False(sends.WasSentChunk(cx, cy, -1));
            Assert.False(sends.WasSentChunk(cx, int.MaxValue, cz));
            Assert.False(sends.WasSentChunk(int.MinValue, int.MinValue, int.MinValue));
            Assert.False(sends.WasSentMapChunk(-1, cz));
            Assert.False(sends.WasSentMapChunk(cx, -1));

            // Beyond the east and south edges of the world, in the overworld only: dimensions
            // have no edge to speak of, so the chunk index of such a position is the engine's own.
            Assert.False(sends.WasSentChunk(columns, cy, cz));
            Assert.False(sends.WasSentMapChunk(columns, cz));
            Assert.False(sends.WasSentMapChunk(cx, world.Api.WorldManager.MapSizeZ / ChunkSize));

            // The positive control the answers above are measured against.
            Assert.True(sends.WasSentChunk(cx, cy, cz));
            Assert.True(sends.WasSentMapChunk(cx, cz));
        });
    }

    [Fact]
    public async Task WasSentChunk_Should_DropTheOverworldColumn_And_KeepTheDimensionSlices_When_ThePlayerMovesAway()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Mover");
            IChunkSendRecord sends = player.ChunkSends;
            IWorldManagerAPI manager = world.Api.WorldManager;
            (int cx, int cy, int cz) = ChunkOf(player.Position);
            int slices = manager.MapSizeY / ChunkSize;
            await world.Until(() => sends.WasSentMapChunk(cx, cz) && sends.WasSentChunk(cx, cy, cz), Bound);

            // A column in dimension 1 under the player, force-sent the way a mod that owns the
            // dimension does it: the slices go, the map chunk does not (the engine's forced path
            // never queues one). A dimension's slice y is the overworld y plus dimension * 1024.
            manager.CreateChunkColumnForDimension(cx, cz, 1);
            manager.ForceSendChunkColumn(player.Player, cx, cz, 1);
            await world.Until(() => Enumerable.Range(0, slices).All(y => sends.WasSentChunk(cx, DimensionStride + y, cz)), Bound);
            for (int y = 0; y < slices; y++)
            {
                Assert.True(manager.HasChunk(cx, DimensionStride + y, cz, player.Player));
            }

            BlockPos away = player.Position.Offset(700, 0, 0);
            await player.TeleportTo(away);
            (int ax, int ay, int az) = ChunkOf(away);
            await world.Until(() => sends.WasSentMapChunk(ax, az) && sends.WasSentChunk(ax, ay, az), Bound);

            // The overworld column is out of range now: the engine unloads it for this player and
            // takes it out of the record, map chunk with it.
            await world.Until(() => !sends.WasSentChunk(cx, cy, cz) && !sends.WasSentMapChunk(cx, cz), Bound);
            Assert.False(manager.HasChunk(cx, cy, cz, player.Player));

            // The dimension's slices are not: the out-of-range pass only looks at the overworld
            // (slices below 128 in the engine's index), so they stay recorded however far the
            // player goes. Let the unload pass run a while longer before trusting that.
            await world.Ticks(60);
            for (int y = 0; y < slices; y++)
            {
                Assert.True(sends.WasSentChunk(cx, DimensionStride + y, cz), $"dimension slice {y} left the record");
            }
        });
    }

    [Fact]
    public async Task WasSentMapChunk_Should_StayFalse_When_TheMapChunkWasForceSent()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Forced");
            IChunkSendRecord sends = player.ChunkSends;
            IWorldManagerAPI manager = world.Api.WorldManager;
            (int cx, int cy, int cz) = ChunkOf(player.Position);
            await world.Until(() => sends.WasSentMapChunk(cx, cz), Bound);

            // A column well outside the view distance, loaded on the server and never sent.
            int fx = cx + 40;
            manager.LoadChunkColumn(fx, cz);
            await world.Until(() => manager.GetMapChunk(fx, cz) != null && manager.GetChunk(fx, cy, cz) != null, Bound);
            Assert.False(sends.WasSentMapChunk(fx, cz));

            long index = ((long)cz * (manager.MapSizeX / ChunkSize)) + fx;
            HashSet<long> queue = ForceSendMapChunkQueue(player);
            manager.ResendMapChunk(fx, cz, onlyIfInRange: false);
            Assert.Contains(index, queue);

            // The engine took the request off the queue, and it only does that for a map chunk it
            // found loaded and put on the wire: a request for one it cannot find goes back on.
            await world.Until(() => !queue.Contains(index), Bound);
            await world.Ticks(5);

            // The packet went, the record did not move: the forced path sends without noting it.
            Assert.False(sends.WasSentMapChunk(fx, cz));
            Assert.False(sends.WasSentChunk(fx, cy, cz));
        });
    }

    [Fact]
    public async Task Queries_Should_KeepAnswering_What_WasRecorded_When_ThePlayerIsGone()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Leaver");
            IChunkSendRecord sends = player.ChunkSends;
            (int cx, int cy, int cz) = ChunkOf(player.Position);
            await world.Until(() => sends.WasSentMapChunk(cx, cz) && sends.WasSentChunk(cx, cy, cz), Bound);

            player.Player.Disconnect("done");
            await world.Until(() => !player.IsConnected, Bound);
            await world.Ticks(30);

            Assert.True(sends.WasSentChunk(cx, cy, cz));
            Assert.True(sends.WasSentMapChunk(cx, cz));
        });
    }

    private static (int Cx, int Cy, int Cz) ChunkOf(BlockPos pos) => (pos.X / ChunkSize, pos.Y / ChunkSize, pos.Z / ChunkSize);

    /// <summary>The player's forced map chunk queue, <c>ConnectedClient.forceSendMapChunks</c>,
    /// through reflection on purpose: a VintagestoryLib type in a test body would resolve at JIT
    /// time, before a host has installed Atlas's AssemblyResolve hook.</summary>
    private static HashSet<long> ForceSendMapChunkQueue(ITestPlayer player)
    {
        FieldInfo clientField = player.Player.GetType().GetField("client", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ServerPlayer.client not found; the engine shape drifted.");
        object client = clientField.GetValue(player.Player)!;
        FieldInfo queueField = client.GetType().GetField("forceSendMapChunks")
            ?? throw new InvalidOperationException("ConnectedClient.forceSendMapChunks not found; the engine shape drifted.");
        return (HashSet<long>)queueField.GetValue(client)!;
    }
}
