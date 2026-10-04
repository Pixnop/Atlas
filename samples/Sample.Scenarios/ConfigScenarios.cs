using System.Net;
using System.Net.Sockets;
using Atlas.Api;
using Atlas.XUnit;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Proves <c>[AtlasDataFiles]</c> seeds files into the server's scratch data path before
/// boot: SampleConfigMod reads <c>ModConfig/sampleconfig.json</c> via <c>api.LoadModConfig</c>
/// inside <c>StartServerSide</c> (the one-shot startup read most config-driven mods use) and
/// the scenario observes the value that read captured. A port a mod listens on goes into the
/// fixture as a <c>{{atlas:port:NAME}}</c> token, which Atlas replaces with a free port per host
/// and hands back through <c>World.DataFilePort</c>.</summary>
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

    // The token sits where the number goes, so fixtures/ModConfig/sampleports.json is not valid
    // JSON until Atlas has seeded it. The mod reads a plain number, and the scenario asks Atlas
    // which one it got instead of freezing a port in a constant that two runs would share.
    [AtlasScenario]
    public async Task DataFilePort_Should_MatchTheConfiguredPort_When_AFixtureHoldsAPortToken()
    {
        SamplePorts? config = World.Api.LoadModConfig<SamplePorts>("sampleports.json");
        await World.Ticks(1);

        Assert.NotNull(config);
        Assert.Equal(World.DataFilePort("metrics"), config.MetricsPort);

        // Free when the mod asks for it: a listener started on it here is what a metrics
        // endpoint in the mod would be.
        var listener = new TcpListener(IPAddress.Loopback, config.MetricsPort);
        listener.Start();
        listener.Stop();
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

    private sealed class SamplePorts
    {
        public int MetricsPort { get; set; }
    }
}
