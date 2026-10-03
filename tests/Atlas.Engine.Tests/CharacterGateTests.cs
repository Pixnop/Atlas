using Atlas.Internal.Bootstrap;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Atlas.Engine.Tests;

/// <summary>The character gate against the real survival mod: with the client listener on, a
/// real connection is marked as having created its character before the survival mod reads the
/// flag, so no dialog is sent to it, while a test player's connection keeps the engine's own
/// flow. The real connection here is <see cref="LoopbackClient"/>, a bare protocol client over a
/// TCP socket: it is a non dummy connection for the engine, which is what the gate keys on, and no
/// game client runs.</summary>
[Trait("Category", "E2E")]
public class CharacterGateTests
{
    private const string FlagKey = "createCharacter";

    [Fact]
    public async Task Gate_Should_RunFirstAndLetOnlyARealConnectionPast_When_TheListenerIsOn()
    {
        await using ServerHost host = new(new WorldOptions(), [], TestPaths.OwnOutputDirectory) { OpenClientListener = true };
        await host.StartAsync();
        ClientEndpoint endpoint = host.ClientEndpoint!;
        TickSource? ticks = null;
        await host.RunOnGameThreadAsync((_, source) =>
        {
            ticks = source;
            return Task.CompletedTask;
        });

        await host.RunScenarioAsync(async world =>
        {
            // The order the engine raises the mod level PlayerJoin handlers in: the bridge's
            // registration first, so before the survival mod's.
            string[] handlers = ListenerProbes.PlayerJoinHandlers(world.Api);
            Assert.StartsWith("Atlas.Bridge.BridgeModsPreSystem", handlers[0]);
            int survival = Array.FindIndex(handlers, h => h.StartsWith("Vintagestory.GameContent.CharacterSystem.", StringComparison.Ordinal));
            Assert.True(survival > 0, $"the survival mod's handler should be registered after the bridge's: {string.Join(", ", handlers)}");

            // A test player is untouched: nothing wrote the flag, so the survival mod took its
            // own default branch and gave it a class. That is what a real client would meet, and
            // answer with the dialog, without the gate.
            ITestPlayer dummy = await world.JoinPlayer("GateDummy");
            Assert.True(ListenerProbes.IsDummyConnection(world.Api, dummy.Player));
            Assert.Null(dummy.Player.GetModdata(FlagKey));
            Assert.False(string.IsNullOrEmpty(dummy.Entity.WatchedAttributes.GetString("characterClass")));

            // A real connection: the flag is true when the survival mod reads it. The proof that
            // the gate ran FIRST is what the survival mod then did not do: it sets the class only
            // when it finds the flag false, and this player has none.
            var joined = new List<string>();
            world.Api.Event.PlayerJoin += player => joined.Add(player.PlayerName);
            using LoopbackClient client = LoopbackClient.Connect(endpoint);
            client.Identify("GateReal", endpoint.Password);
            await world.Until(() => Find(world, "GateReal") is { Entity: not null });
            client.RequestJoin();
            await world.Until(() => joined.Contains("GateReal"));

            IServerPlayer real = Find(world, "GateReal")!;
            Assert.False(ListenerProbes.IsDummyConnection(world.Api, real));
            Assert.True(SerializerUtil.Deserialize<bool>(real.GetModdata(FlagKey), false));
            Assert.Null(real.Entity.WatchedAttributes.GetString("characterClass"));

            // With nothing held back by a dialog, the packets a loaded client sends take it to Playing.
            client.SendClientLoadedAndReady();
            IServerPlayer playing = await PlayingWatch.WaitForPlayingAsync(ticks!, () => Find(world, "GateReal"));
            Assert.Equal(EngineCompat.ClientStatePlaying, playing.ConnectionState);
        });
    }

    private static IServerPlayer? Find(IWorldSession world, string name)
        => world.Api.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault(p => p.PlayerName == name);
}
