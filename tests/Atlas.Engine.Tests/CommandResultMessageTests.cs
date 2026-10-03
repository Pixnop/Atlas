using Vintagestory.API.Common;

namespace Atlas.Engine.Tests;

/// <summary>Pins whose text <see cref="CommandResult.Message"/> holds, against the live engine: the
/// engine's own status message when the command gave one, Atlas's "Command '...' failed with
/// status ..." sentence when it failed without one, and nothing for a success that said nothing.
/// The rule is documented on the type, and an <c>Assert.Contains</c> on <c>Message</c> can match
/// the second kind, which is what these cases keep visible. Driven against PlayerCommandFixtureMod's
/// <c>/callerfx</c> and <c>/legacyfx</c>, plus a name nothing registers.</summary>
[Trait("Category", "E2E")]
public class CommandResultMessageTests
{
    private const string FixtureModDll = "PlayerCommandFixtureMod.dll";

    [Fact]
    public async Task Message_Should_HoldTheEnginesText_When_TheCommandSucceedsWithAMessage()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Caller");

            CommandResult result = await player.ExecuteCommand("/callerfx deferred hello");

            Assert.True(result.Ok, result.Message);
            Assert.Equal("echo:hello", result.Raw.StatusMessage);
            Assert.Equal("echo:hello", result.Message);
        });
    }

    [Fact]
    public async Task Message_Should_HoldTheEnginesText_When_TheCommandFailsWithAMessage()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("Caller");

            // A handler's own refusal, with no code: the message is the handler's, not Atlas's.
            CommandResult noCode = await player.ExecuteCommand("/callerfx bogus");
            Assert.False(noCode.Ok);
            Assert.Equal("unknown op 'bogus'", noCode.Raw.StatusMessage);
            Assert.Equal("unknown op 'bogus'", noCode.Message);
            Assert.Equal(string.Empty, noCode.ErrorCode);

            // A handler's refusal with a code: still the handler's text, and the code is separate.
            player.Player.SetRole("suplayer");
            CommandResult withCode = await player.ExecuteCommand("/callerfx admin");
            Assert.False(withCode.Ok);
            Assert.Equal("missing controlserver", withCode.Raw.StatusMessage);
            Assert.Equal("missing controlserver", withCode.Message);
            Assert.Equal("noprivilege", withCode.ErrorCode);

            // The console caller carries no player, so RequiresPlayer refuses before the handler
            // runs. That refusal has a message of the engine's own, and no code.
            CommandResult refused = await world.ExecuteCommand("/callerfx whoami");
            Assert.False(refused.Ok);
            Assert.Equal("Caller must be player", refused.Raw.StatusMessage);
            Assert.Equal("Caller must be player", refused.Message);
            Assert.Equal(string.Empty, refused.ErrorCode);
        });
    }

    [Fact]
    public async Task Message_Should_HoldAtlasText_When_TheCommandFailsWithoutAMessage()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Nothing is registered under this name: the engine reports a status and a code and
            // no message, so the sentence is Atlas's, and carries both.
            CommandResult missing = await world.ExecuteCommand("/nosuchcommandatall");
            Assert.False(missing.Ok);
            Assert.Null(missing.Raw.StatusMessage);
            Assert.Equal(EnumCommandStatus.NoSuchCommand, missing.Status);
            Assert.Equal(
                "Command '/nosuchcommandatall' failed with status 'NoSuchCommand' and error code 'nosuchcommand'.",
                missing.Message);

            // A legacy command runs, and the engine still reports UnknownLegacy with no message,
            // so Ok is false and the sentence says "failed" about a command that ran.
            CommandResult legacy = await world.ExecuteCommand("/legacyfx");
            Assert.True(world.Api.World.Config.GetBool("legacyfxran"), "the legacy handler did not run");
            Assert.False(legacy.Ok);
            Assert.Null(legacy.Raw.StatusMessage);
            Assert.Equal("Command '/legacyfx' failed with status 'UnknownLegacy'.", legacy.Message);
        });
    }
}
