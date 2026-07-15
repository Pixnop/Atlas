using System.Linq;
using Atlas.Internal.Bootstrap;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Regression coverage for issue #74: joined test players must complete the engine's
/// own join sequence (packets 26/29) and reach <c>EnumClientState.Playing</c>, so server code
/// filtering on <c>ConnectedClient.IsPlayingClient</c> (distance-based throttling like
/// Stratum's, playing-count broadcasts, <c>NearestPlayer</c>) sees them instead of an
/// apparently empty server.</summary>
/// <remarks>The playing count is asserted through TWO engine-owned filters:
/// <c>ServerMain.GetPlayingClients()</c>, the engine's own server-side count (it counts
/// <c>State == EnumClientState.Playing</c>), and a direct iteration of <c>ServerMain.Clients</c>
/// reading <c>ConnectedClient.IsPlayingClient</c> per client - the exact flag and iteration
/// shape Stratum's distance-based throttling consumes (the issue #74 field report). Both are
/// present unchanged on 1.20.12, 1.21.7 and 1.22.3. They are read through reflection on
/// purpose: referencing VintagestoryLib types in a test method body would make the CLR resolve
/// them at JIT time, before any host has booted and installed Atlas's AssemblyResolve hook, and
/// copying VintagestoryLib into this project's output (the VintagestoryAPI workaround) would
/// change assembly probing for every host the whole suite boots. The kick and rollback player
/// suites are the regression net for the transition's side effects (Playing-player teardown,
/// player-aware rollback).</remarks>
[Trait("Category", "E2E")]
public class PlayingStateTests
{
    [Fact]
    public async Task JoinPlayer_Should_BeCountedByEnginePlayingFilter_When_Joined()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Pin that the server-side transition actually RAN, not just that a state flag got
            // set: HandleClientLoaded (packet 26) is the engine's only call site of the
            // PlayerNowPlaying event, so observing it proves the real handler executed.
            var nowPlaying = new List<string>();
            world.Api.Event.PlayerNowPlaying += p => nowPlaying.Add(p.PlayerName);

            ITestPlayer alice = await world.JoinPlayer("PlayingAlice");
            ITestPlayer bob = await world.JoinPlayer("PlayingBob");

            // Playing through EngineCompat, never the literal: 1.22 shifted the enum's
            // values, and this suite is itself run PREBUILT across installs (issue #49).
            Assert.Equal(EngineCompat.ClientStatePlaying, alice.Player.ConnectionState);
            Assert.Equal(EngineCompat.ClientStatePlaying, bob.Player.ConnectionState);
            Assert.Equal(2, EnginePlayingClientCount(world));
            Assert.Equal(2, IsPlayingClientCount(world));
            Assert.Contains("PlayingAlice", nowPlaying);
            Assert.Contains("PlayingBob", nowPlaying);

            // The API-level view mods commonly filter on agrees with the engine-side count.
            Assert.Equal(
                2,
                world.Api.World.AllOnlinePlayers
                    .OfType<IServerPlayer>()
                    .Count(p => p.ConnectionState == EngineCompat.ClientStatePlaying));
        });
    }

    [Fact]
    public async Task Kick_Should_RemovePlayerFromEnginePlayingFilter_When_PlayingPlayerKicked()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer stays = await world.JoinPlayer("PlayingStays");
            ITestPlayer kicked = await world.JoinPlayer("PlayingKicked");
            Assert.Equal(2, EnginePlayingClientCount(world));

            // Game-thread kick of a fully Playing player: the teardown must remove it from the
            // engine's playing filter without touching the other player.
            kicked.Player.Disconnect("kicked while playing");
            await world.Until(() => !kicked.IsConnected);

            Assert.Equal(1, EnginePlayingClientCount(world));
            Assert.Equal(1, IsPlayingClientCount(world));
            Assert.True(stays.IsConnected);
            Assert.Equal(EngineCompat.ClientStatePlaying, stays.Player.ConnectionState);
        });
    }

    /// <summary>Counts playing clients through <c>ServerMain.GetPlayingClients()</c>, the
    /// engine's own server-side filter (see class remarks for why reflection).</summary>
    private static int EnginePlayingClientCount(IWorldSession world)
    {
        object serverMain = world.Api.World;
        var method = serverMain.GetType().GetMethod("GetPlayingClients")
            ?? throw new InvalidOperationException(
                "ServerMain.GetPlayingClients() not found; the engine shape drifted.");
        return (int)method.Invoke(serverMain, null)!;
    }

    /// <summary>Counts playing clients the way Stratum's distance-based throttling does: iterate
    /// <c>ServerMain.Clients</c> and read each <c>ConnectedClient.IsPlayingClient</c> - the exact
    /// flag the issue #74 field report named as the acceptance criterion (see class remarks for
    /// why reflection).</summary>
    private static int IsPlayingClientCount(IWorldSession world)
    {
        object serverMain = world.Api.World;
        object clients = serverMain.GetType().GetField("Clients")?.GetValue(serverMain)
            ?? throw new InvalidOperationException(
                "ServerMain.Clients not found; the engine shape drifted.");

        int playing = 0;
        foreach (object entry in (System.Collections.IEnumerable)clients)
        {
            // Entries are KeyValuePair<int, ConnectedClient>.
            object connectedClient = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            var isPlayingClient = connectedClient.GetType().GetProperty("IsPlayingClient")
                ?? throw new InvalidOperationException(
                    "ConnectedClient.IsPlayingClient not found; the engine shape drifted.");
            if ((bool)isPlayingClient.GetValue(connectedClient)!)
            {
                playing++;
            }
        }

        return playing;
    }
}
