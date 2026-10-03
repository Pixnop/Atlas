using Atlas.Internal.Bootstrap;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Pins why <see cref="PlayingWatch"/> polls the connection state: the engine raises
/// <c>PlayerReady</c> when a client reaches <c>Playing</c>, but never to a mod, so the event
/// cannot be waited on. Runs on test players, which reach <c>Playing</c> by the same packets (26
/// and 29) a game client sends.</summary>
/// <remarks>A tripwire as much as a test: if a game version starts forwarding the event to mods,
/// the first fact fails on that version, which is the cue to revisit the helper's comment and this
/// class, not a regression of Atlas.</remarks>
[Trait("Category", "E2E")]
public class PlayingDetectionTests
{
    private const string Name = "PlayingDummy";

    private const string ForwardedMessage =
        "api.Event.PlayerReady reached a mod: this game version forwards the event, so PlayingWatch's comment " +
        "and this test can be revisited (polling still works either way).";

    [Fact]
    public async Task PlayerReady_Should_NotReachAMod_When_AJoinedPlayerReachesPlaying()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            int ready = 0;
            int nowPlaying = 0;

            // PlayerReady exists from 1.22 on; reflection lets the one binary subscribe where it
            // does, like the Playing value itself (see EngineCompat.ClientStatePlaying).
            System.Reflection.EventInfo? readyEvent = world.Api.Event.GetType().GetEvent("PlayerReady");
            readyEvent?.AddEventHandler(world.Api.Event, (PlayerDelegate)(_ => ready++));

            // The control: packet 26's event does reach mods.
            world.Api.Event.PlayerNowPlaying += _ => nowPlaying++;

            ITestPlayer player = await world.JoinPlayer(Name);

            Assert.Equal(EngineCompat.ClientStatePlaying, player.Player.ConnectionState);
            Assert.Equal(1, nowPlaying);
            Assert.True(ready == 0, ForwardedMessage);

            // Where the event is absent, that is the pre-1.22 engines, not a drift of 1.22.
            Assert.True(
                readyEvent != null || !EngineCompat.ShortGameVersion.StartsWith("1.22", StringComparison.Ordinal),
                "api.Event.PlayerReady is gone from a 1.22 engine.");
        });
    }

    [Fact]
    public async Task WaitForPlayerAsync_Should_FindAJoinedTestPlayer_When_ItIsPlaying()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        TickSource? ticks = null;
        await host.RunOnGameThreadAsync((_, source) =>
        {
            ticks = source;
            return Task.CompletedTask;
        });

        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(Name);

            IServerPlayer found = await PlayingWatch.WaitForPlayerAsync(
                ticks!,
                () => world.Api.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault(p => p.PlayerName == Name),
                timeoutTicks: 10);

            Assert.Equal(player.Player.PlayerUID, found.PlayerUID);
            Assert.True(PlayingWatch.IsPlaying(found.ConnectionState));

            // Nobody by that name: the wait runs out instead of returning someone else.
            await Assert.ThrowsAsync<ScenarioTimeoutException>(
                () => PlayingWatch.WaitForPlayerAsync(
                    ticks!,
                    () => world.Api.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault(p => p.PlayerName == "NobodyByThisName"),
                    timeoutTicks: 5));
        });
    }
}
