using Atlas.Internal.Rollback;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Regression coverage for the rollback-versus-chunk-streaming database race exposed
/// by Playing test players (issue #74 follow-up): the engine's chunkdbthread reads and writes
/// the savegame connection, lock-free, while it streams chunks to a Playing client, and a
/// rollback that touches the same connection from the game thread without the engine's suspend
/// window made those reads throw ("Execute requires the command to have a transaction object
/// when the connection ... is in a pending local transaction" on 1.22.x), which the engine
/// escalates to a full server shutdown. The scenario here is the closest deterministic proxy:
/// teleport the Playing player deep into never-loaded terrain, so a view distance of fresh
/// columns is still streaming on the chunkdbthread, and trigger a rollback restore immediately.
/// On the unfixed code under CPU contention (taskset, 4 cores) this crashed the engine; with
/// the suspend window it must restore cleanly, repeatedly.</summary>
[Trait("Category", "E2E")]
public class RollbackChunkStreamingTests
{
    [Fact]
    public async Task TryRollbackWorld_Should_RestoreCleanly_When_APlayingPlayerIsStreamingFreshChunks()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();

        ITestPlayer player = null!;
        BlockPos baselinePos = null!;
        await host.RunScenarioAsync(async world =>
        {
            player = await world.JoinPlayer("Streamer");
            await world.Ticks(2);
            baselinePos = player.Position;
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture with a joined player failed");

        // Three rounds, each into a different never-loaded area: TeleportTo only waits for the
        // target column itself, so when it returns, the surrounding view distance is still
        // loading and generating on the chunkdbthread; the rollback right behind it must
        // serialize against that streaming instead of racing it.
        for (int round = 1; round <= 3; round++)
        {
            await host.RunScenarioAsync(async world =>
            {
                await player.TeleportTo(baselinePos.Offset(640 * round, 0, 640 * round));
                Assert.NotEqual(baselinePos, player.Position);
            });

            RollbackAttempt attempt = await host.TryRollbackWorldAsync();
            Assert.True(
                attempt.Succeeded,
                $"rollback during active chunk streaming failed (round {round}): {attempt.DegradeDetail}");
            Assert.Null(host.CrashException);
        }

        // The world survived three streaming rollbacks: the player is back at its captured
        // baseline and the host can still run scenarios.
        await host.RunScenarioAsync(async world =>
        {
            await world.Ticks(2);
            Assert.True(player.IsConnected, "the captured player must survive the rollbacks");
            Assert.Equal(baselinePos, player.Position);
        });
    }
}
