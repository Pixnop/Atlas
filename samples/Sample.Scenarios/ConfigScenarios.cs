using Atlas.Api;
using Atlas.XUnit;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Proves <c>[AtlasDataFiles]</c> seeds files into the server's scratch data path before
/// boot: SampleConfigMod reads <c>ModConfig/sampleconfig.json</c> via <c>api.LoadModConfig</c>
/// inside <c>StartServerSide</c> (the one-shot startup read most config-driven mods use) and
/// the scenario observes the value that read captured.</summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]
public class ConfigScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task LoadModConfig_Should_SeeSeededConfigFile_When_ReadDuringStartServerSide()
    {
        CommandResult result = await World.ExecuteCommand("/sampleconfig");

        Assert.True(result.Ok, result.Message);
        Assert.Equal("hello-from-atlas-fixture", result.Message);
    }

    // Same command, the other caller. ExecuteCommand runs it as a console caller and hands back
    // the handler's return value; a joined player typing it gets the reply through the chat path
    // instead, which is what a mod's users actually see. The two can disagree (a handler that
    // messages the caller directly returns nothing to the console), so a mod with a
    // player-facing command is worth asserting from both sides.
    [AtlasScenario]
    public async Task PlayerChat_Should_ReceiveTheCommandReply_When_ATestPlayerRunsTheCommand()
    {
        ITestPlayer player = await World.JoinPlayer("Tester");

        await player.Say("/sampleconfig");

        // Containment, not equality: the engine's chat formatting wraps the reply line.
        Assert.Contains(player.Client.ChatLines(), line => line.Contains("hello-from-atlas-fixture"));
    }
}
