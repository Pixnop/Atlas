using ClientCaptureFixtureMod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers the client observation surface (spec docs/specs/2026-07-17-client-observations.md)
/// end to end: what the embedded server sends a joined test player, captured off the player's own
/// dummy connection and decoded as a client would. Highlights, particles and chat lines come from
/// the engine's own APIs; mod-channel packets from ClientCaptureFixtureMod, a staged dll loaded by
/// the game's ModLoader that registers channel <c>atlasfixture</c> with one protobuf message
/// exactly the way a shipping mod does (issue #100, the Caminus field request).</summary>
[Trait("Category", "E2E")]
public class ClientObservationTests
{
    private const string FixtureModDll = "ClientCaptureFixtureMod.dll";
    private const string PlayerName = "AtlasObserver";
    private const int OverlaySlot = 7;

    /// <summary>The test project's own output directory, not <c>AppContext.BaseDirectory</c>
    /// (the first host boot redirects that to the game install), where the fixture mod dll lives.</summary>
    private static string OutputDirectory => Path.GetDirectoryName(typeof(ClientObservationTests).Assembly.Location)!;

    [Fact]
    public async Task Highlights_Should_ExposePositionsAndColorsPerSlot_And_ClearTheSlot_When_TheServerHighlightsNoBlocks()
    {
        await using var host = NewHost();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);
            var positions = new List<BlockPos> { world.Spawn.Offset(1, 0, 0), world.Spawn.Offset(2, 0, 0) };
            var colors = new List<int> { ColorUtil.ColorFromRgba(255, 0, 0, 128), ColorUtil.ColorFromRgba(0, 0, 255, 200) };

            world.Api.World.HighlightBlocks(player.Player, OverlaySlot, positions, colors);
            world.Api.World.HighlightBlocks(player.Player, OverlaySlot + 1, positions, new List<int> { ColorUtil.ColorFromRgba(0, 255, 0, 255) });

            // Captured synchronously by the send: no ticks needed.
            IReadOnlyList<HighlightedBlock> overlay = player.Client.Highlights(OverlaySlot);
            Assert.Equal(positions, overlay.Select(b => b.Pos).ToList());
            Assert.Equal(colors, overlay.Select(b => b.Color).ToList());
            Assert.Equal(new Rgba(255, 0, 0, 128), overlay[0].Rgba);
            Assert.Equal(new Rgba(0, 0, 255, 200), overlay[1].Rgba);

            // A single color applies to every position, and slots are independent.
            IReadOnlyList<HighlightedBlock> other = player.Client.Highlights(OverlaySlot + 1);
            Assert.Equal(2, other.Count);
            Assert.All(other, b => Assert.Equal(new Rgba(0, 255, 0, 255), b.Rgba));

            // The latest packet wins: an empty highlight clears the slot, the other slot stays.
            world.Api.World.HighlightBlocks(player.Player, OverlaySlot, new List<BlockPos>(), new List<int>());
            Assert.Empty(player.Client.Highlights(OverlaySlot));
            Assert.Equal(2, player.Client.Highlights(OverlaySlot + 1).Count);
            Assert.Empty(player.Client.Highlights(42));
        });
    }

    [Fact]
    public async Task Particles_Should_ExposeTheSpawnProperties_When_TheServerSpawnsParticlesInAStreamedChunk()
    {
        await using var host = NewHost();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);
            BlockPos at = player.Position.Offset(0, 1, 0);
            var minPos = new Vec3d(at.X, at.Y, at.Z);
            var maxPos = new Vec3d(at.X + 1, at.Y + 1, at.Z + 1);
            var minVelocity = new Vec3f(0, 0.5f, 0);
            int color = ColorUtil.ToRgba(200, 255, 10, 20);

            // The engine only sends a spawn to players it already streamed the chunk to, and the
            // join returns before chunk streaming settles: spawn once per tick until one lands.
            await world.Until(
                () =>
                {
                    world.Api.World.SpawnParticles(5f, color, minPos, maxPos, minVelocity, new Vec3f(0, 1, 0), 1.5f, 0.25f);
                    return player.Client.Particles().Count > 0;
                },
                timeoutTicks: 600);

            SpawnedParticles spawn = player.Client.Particles()[0];
            Assert.Equal("simple", spawn.ProviderClassName);
            Assert.Equal(minPos, spawn.Position);
            Assert.Equal(minVelocity, spawn.Velocity);
            Assert.Equal(5f, spawn.Quantity);
            Assert.Equal(color, spawn.Color);
            Assert.Equal(new Rgba(255, 10, 20, 200), spawn.Rgba);
            var simple = Assert.IsType<SimpleParticleProperties>(spawn.Provider);
            Assert.Equal(1.5f, simple.LifeLength);
            Assert.Equal(0.25f, simple.GravityEffect);
        });
    }

    [Fact]
    public async Task Packets_Should_DecodeModChannelMessages_When_TheModSendsThemOnJoinAndOnCommand()
    {
        await using var host = NewHost(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            // Sent by the mod's PlayerNowPlaying handler, during the join sequence itself.
            AtlasFixtureMessage welcome = Assert.Single(player.Client.Packets<AtlasFixtureMessage>("atlasfixture"));
            Assert.Equal("welcome " + PlayerName, welcome.Text);

            CommandResult sent = await world.ExecuteCommand($"/atlasfixture send {PlayerName} overlay: 21.5 C");
            Assert.True(sent.Ok, sent.Message);

            IReadOnlyList<AtlasFixtureMessage> packets = player.Client.Packets<AtlasFixtureMessage>("atlasfixture");
            Assert.Equal(2, packets.Count);
            Assert.Equal("overlay: 21.5 C", packets[1].Text);

            player.Client.Clear();
            Assert.Empty(player.Client.Packets<AtlasFixtureMessage>("atlasfixture"));
        });
    }

    [Fact]
    public async Task Packets_Should_ThrowActionableArgumentException_When_TheChannelOrMessageTypeIsUnknown()
    {
        await using var host = NewHost(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            ArgumentException unknownChannel = Assert.Throws<ArgumentException>(
                () => player.Client.Packets<AtlasFixtureMessage>("caminus"));
            Assert.Contains("'caminus'", unknownChannel.Message);
            Assert.Contains("RegisterChannel", unknownChannel.Message);

            ArgumentException unknownType = Assert.Throws<ArgumentException>(
                () => player.Client.Packets<CommandResult>("atlasfixture"));
            Assert.Contains(typeof(CommandResult).FullName!, unknownType.Message);
            Assert.Contains(typeof(AtlasFixtureMessage).FullName!, unknownType.Message);
        });
    }

    [Fact]
    public async Task ChatLines_Should_ContainMessagesSentToThePlayer_And_Clear_Should_ForgetThem()
    {
        await using var host = NewHost();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            world.Api.SendMessage(player.Player, GlobalConstants.GeneralChatGroup, "hello from atlas", EnumChatType.Notification);

            Assert.Contains("hello from atlas", player.Client.ChatLines());

            player.Client.Clear();
            Assert.Empty(player.Client.ChatLines());

            world.Api.SendMessage(player.Player, GlobalConstants.GeneralChatGroup, "after clear", EnumChatType.Notification);
            Assert.Equal(["after clear"], player.Client.ChatLines());
        });
    }

    [Fact]
    public async Task Say_Should_RunACommandThroughTheRealChatPath_And_RouteTheReplyBackToTheSender()
    {
        await using var host = NewHost(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            // The fixture's /atlasfixture command requires controlserver, which the default
            // "suplayer" role a fresh join gets does not carry; a real command tester would
            // instead pick a command their own player's role already allows.
            player.Player.SetRole("admin");

            await player.Say($"/atlasfixture send {PlayerName} overlay: 21.5 C");

            // The command's own reply, routed back by the engine's real command-dispatch path
            // (ChatCommandApi.Execute calling player.SendMessage on the calling player) - not
            // observable through world.ExecuteCommand, whose synthetic console caller has no
            // player behind it for that routing to target.
            Assert.Contains("sent to " + PlayerName, player.Client.ChatLines());

            // The channel packet the command handler sent as a side effect, decoded the same way
            // Packets_Should_DecodeModChannelMessages... reads the join-time welcome packet.
            IReadOnlyList<AtlasFixtureMessage> packets = player.Client.Packets<AtlasFixtureMessage>("atlasfixture");
            Assert.Contains(packets, p => p.Text == "overlay: 21.5 C");
        });
    }

    [Fact]
    public async Task Say_Should_SendAPlainChatLine_And_TheServerEchoesItBackToTheSender()
    {
        await using var host = NewHost();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            await player.Say("hello from the test player");

            // HandleChatMessage echoes a plain (non-command) line back to its own sender as
            // EnumChatType.OwnMessage, on top of broadcasting it to the rest of the group. The
            // engine wraps the echo with a "<strong>Name:</strong> " prefix (the chat UI's own
            // formatting), so this checks containment, not an exact line.
            Assert.Contains(player.Client.ChatLines(), line => line.Contains("hello from the test player"));
        });
    }

    [Fact]
    public async Task Client_Should_ClearObservations_When_TheWorldIsRolledBack()
    {
        await using var host = NewHost();
        await host.StartAsync();

        // Joined before the capture, so the rollback keeps the player (and its observations
        // object) alive across the restore.
        ITestPlayer player = null!;
        await host.RunScenarioAsync(async world => player = await world.JoinPlayer(PlayerName));
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        await host.RunScenarioAsync(world =>
        {
            world.Api.World.HighlightBlocks(player.Player, OverlaySlot, new List<BlockPos> { world.Spawn }, new List<int> { 1 });
            world.Api.SendMessage(player.Player, GlobalConstants.GeneralChatGroup, "before rollback", EnumChatType.Notification);
            Assert.Single(player.Client.Highlights(OverlaySlot));
            Assert.Contains("before rollback", player.Client.ChatLines());
            return Task.CompletedTask;
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(world =>
        {
            Assert.True(player.IsConnected, "the rollback dropped a player joined before the capture");
            Assert.Empty(player.Client.Highlights(OverlaySlot));
            Assert.DoesNotContain("before rollback", player.Client.ChatLines());
            return Task.CompletedTask;
        });
    }

    private static ServerHost NewHost(params string[] mods)
        => new(new WorldOptions(), mods, OutputDirectory);
}
