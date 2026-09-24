using System.Globalization;
using Vintagestory.API.Common;

namespace Atlas.Engine.Tests;

/// <summary>Covers <see cref="ITestPlayer.ExecuteCommand"/>: a command run with a joined test
/// player as caller instead of the synthetic console <see cref="IWorldSession.ExecuteCommand"/>
/// uses. Driven against PlayerCommandFixtureMod's <c>/callerfx</c>, which is
/// <c>RequiresPlayer</c>, gates one op behind a privilege a downgraded caller lacks, reads one
/// op's argument through a parser that always defers, and reports the caller's own position: the
/// three things consumer test suites each worked around on their own, plus the position proof
/// this lane added.</summary>
[Trait("Category", "E2E")]
public class PlayerExecuteCommandTests
{
    private const string FixtureModDll = "PlayerCommandFixtureMod.dll";
    private const string PlayerName = "Caller";

    [Fact]
    public async Task ExecuteCommand_Should_BeRefusedForTheConsole_And_AcceptedForThePlayer_When_TheCommandRequiresPlayer()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            // The console caller carries no player, so a RequiresPlayer command refuses it.
            CommandResult refused = await world.ExecuteCommand("/callerfx whoami");
            Assert.False(refused.Ok, refused.Message);

            // RequiresPlayer's own precondition rejects the call before the handler ever runs,
            // and (unlike the fixture's own "admin" refusal) leaves ErrorCode unset: asserting
            // that pins this as the RequiresPlayer refusal specifically, not some other failure.
            Assert.Equal(string.Empty, refused.Raw.ErrorCode);

            CommandResult accepted = await player.ExecuteCommand("/callerfx whoami");
            Assert.True(accepted.Ok, accepted.Message);

            // The handler sees this exact player, not a synthetic stand-in: name and uid match.
            Assert.Equal($"{player.Player.PlayerName}:{player.Player.PlayerUID}", accepted.Message);
        });
    }

    [Fact]
    public async Task ExecuteCommand_Should_BeRefused_When_ThePlayerLacksThePrivilege_And_Accepted_After_ItIsGranted()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            // A joined test player rides the same dummy-socket path real singleplayer does, so
            // the engine hands it the highest-privilege role (IsSinglePlayerClient) regardless of
            // the server's own configured default: it starts out holding controlserver, not
            // lacking it. Downgrading to suplayer (chat, no controlserver) opens a real gap to
            // prove this caller is checked against, then SetRole restores it: this caller runs
            // with whatever role the player actually, currently holds, never a forced stand-in.
            player.Player.SetRole("suplayer");

            CommandResult refused = await player.ExecuteCommand("/callerfx admin");
            Assert.False(refused.Ok);
            Assert.Equal("noprivilege", refused.Raw.ErrorCode);

            player.Player.SetRole("admin");

            CommandResult accepted = await player.ExecuteCommand("/callerfx admin");
            Assert.True(accepted.Ok, accepted.Message);
        });
    }

    [Fact]
    public async Task ExecuteCommand_Should_ReturnTheFinalResult_Not_Deferred_When_ArgumentParsingGoesAsync()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            CommandResult result = await player.ExecuteCommand("/callerfx deferred hello");

            Assert.True(result.Ok, result.Message);
            Assert.Equal("echo:hello", result.Message);
            Assert.Equal(EnumCommandStatus.Success, result.Raw.Status);
        });
    }

    [Fact]
    public async Task ExecuteCommand_Should_SeeThePlayersRealPosition_When_NoTeleportHasHappened()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer(PlayerName);

            // Deliberately no TeleportTo here: pre-1.22 engines leave a headless player's
            // Entity.Pos at the world origin until something teleports it, even though the
            // player's real (sided) position is already the spawn point. This is the case that
            // catches a caller built without the sided-position fix.
            CommandResult result = await player.ExecuteCommand("/callerfx pos");
            Assert.True(result.Ok, result.Message);

            string[] parts = result.Message.Split(',');
            double x = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double y = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double z = double.Parse(parts[2], CultureInfo.InvariantCulture);

            // Compared against the sided position ITestPlayer.Position already reads correctly,
            // with a one-block tolerance for BlockPos's integer rounding. A stale, unfixed caller
            // reports the origin here, thousands of blocks off a real spawn point.
            Assert.InRange(x, player.Position.X - 1, player.Position.X + 1);
            Assert.InRange(y, player.Position.Y - 1, player.Position.Y + 1);
            Assert.InRange(z, player.Position.Z - 1, player.Position.Z + 1);
        });
    }
}
