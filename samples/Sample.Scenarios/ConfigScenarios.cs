using Atlas.Api;
using Atlas.XUnit;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Proves <c>[AtlasDataFiles]</c> seeds files into the server's scratch data path before
/// boot: SampleConfigMod reads <c>ModConfig/sampleconfig.json</c> via <c>api.LoadModConfig</c>
/// inside <c>StartServerSide</c> — the one-shot startup read most config-driven mods use — and
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
}
