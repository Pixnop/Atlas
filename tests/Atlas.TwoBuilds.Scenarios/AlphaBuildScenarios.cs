using Atlas.XUnit;
using Xunit;

namespace Atlas.TwoBuilds.Scenarios;

/// <summary>Stages the alpha build of the mod, and nothing else: the assembly's own mods are
/// excluded, so the only copy of the mod identity in play is the staged one.</summary>
[AtlasWorld(Mods = ["build-alpha/BindingFixtureMod.dll"], ExcludeAssemblyMods = true)]
public class AlphaBuildScenarios : TwoBuildsScenarioBase
{
    [AtlasScenario]
    public Task Alpha_Should_RunTheAlphaBuild_When_ItIsTheOnlyBuildInTheProcess()
    {
        Assert.Equal("alpha", RunningBuild());
        return Task.CompletedTask;
    }
}
