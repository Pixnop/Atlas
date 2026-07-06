using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Regression coverage for kicked test players (the zombie-player issue): a mod under
/// test kicking a joined dummy player must result in a clean, complete removal - no client left
/// in <c>AllOnlinePlayers</c>, no still-ticking half-despawned entity, and a usable
/// <see cref="ITestPlayer.IsConnected"/> signal - regardless of which thread the mod kicked
/// from.</summary>
/// <remarks>The off-thread variant mirrors the real-world trigger (Nimbus.ServerMod kicks from a
/// thread-pool continuation after an HTTP reservation check, wrapped in a swallowing catch):
/// <c>ServerMain.DisconnectPlayer</c> off the game thread dies on a NullReferenceException in
/// <c>DespawnEntity</c> (<c>ServerMain.FrameProfiler</c> is thread-static, so it is null off the
/// game thread), aborting the teardown after the PlayerDisconnect event but before the client
/// and entity registries are cleaned. A real TCP client self-heals because its socket close
/// re-runs the teardown on the game thread; Atlas's dummy socket has no close, so
/// <c>KickedPlayerCleanup</c> supplies that second run. These tests pin both halves.</remarks>
[Trait("Category", "E2E")]
public class KickedPlayerTests
{
    [Fact]
    public async Task Kick_Should_RemovePlayerCompletely_When_KickedFromBackgroundThread()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ICoreServerAPI api = world.Api;
            api.Event.PlayerJoin += joined =>
            {
                if (joined.PlayerName != "KickedOffThread")
                {
                    return;
                }

                // Mirror the Nimbus.ServerMod pattern exactly: kick from a thread-pool thread
                // (after an awaited check), exception swallowed by the mod's own catch. The
                // swallowed exception is the zombie-maker this suite guards against.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    try
                    {
                        joined.Disconnect("kicked off-thread");
                    }
                    catch
                    {
                        // Swallow, like the real mod does.
                    }
                });
            };

            ITestPlayer player = await world.JoinPlayer("KickedOffThread");
            long entityId = player.Entity.EntityId;

            await world.Until(() => !player.IsConnected);

            Assert.DoesNotContain(api.World.AllOnlinePlayers, p => p.PlayerName == "KickedOffThread");
            Assert.Equal(EnumClientState.Offline, player.Player.ConnectionState);
            Assert.Equal(EnumEntityState.Despawned, player.Entity.State);
            Assert.Null(api.World.GetEntityById(entityId));
        });
    }

    [Fact]
    public async Task Kick_Should_RemovePlayerCompletely_When_KickedOnGameThreadDuringJoin()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ICoreServerAPI api = world.Api;
            api.Event.PlayerJoin += joined =>
            {
                if (joined.PlayerName == "KickedOnJoin")
                {
                    joined.Disconnect("kicked on join");
                }
            };

            ITestPlayer player = await world.JoinPlayer("KickedOnJoin");
            long entityId = player.Entity.EntityId;

            await world.Until(() => !player.IsConnected);

            Assert.DoesNotContain(api.World.AllOnlinePlayers, p => p.PlayerName == "KickedOnJoin");
            Assert.Equal(EnumClientState.Offline, player.Player.ConnectionState);
            Assert.Equal(EnumEntityState.Despawned, player.Entity.State);
            Assert.Null(api.World.GetEntityById(entityId));
        });
    }

    [Fact]
    public async Task Kick_Should_AllowRejoinUnderSameName_When_RemovalCompleted()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ICoreServerAPI api = world.Api;
            bool kickArmed = true;
            api.Event.PlayerJoin += joined =>
            {
                if (kickArmed && joined.PlayerName == "Rebound")
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            joined.Disconnect("kicked once");
                        }
                        catch
                        {
                            // Swallow, like the real mod does.
                        }
                    });
                }
            };

            ITestPlayer first = await world.JoinPlayer("Rebound");
            await world.Until(() => !first.IsConnected);
            kickArmed = false;

            ITestPlayer second = await world.JoinPlayer("Rebound");

            Assert.True(second.IsConnected);
            Assert.NotNull(second.Entity);
            IReadOnlyList<Entity> nearby = world.EntitiesIn(second.Position.Area(16));
            Assert.Contains(nearby, e => e.EntityId == second.Entity.EntityId);
        });
    }
}
