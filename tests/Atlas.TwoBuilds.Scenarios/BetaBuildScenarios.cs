using Atlas.XUnit;
using Xunit;

namespace Atlas.TwoBuilds.Scenarios;

/// <summary>Stages the beta build, the same assembly identity as
/// <see cref="AlphaBuildScenarios"/> stages the alpha build of.</summary>
[AtlasWorld(Mods = ["build-beta/BindingFixtureMod.dll"], ExcludeAssemblyMods = true)]
public class BetaBuildScenarios : TwoBuildsScenarioBase
{
    [AtlasScenario]
    public Task Beta_Should_RunTheBetaBuild_When_ItIsTheOnlyBuildInTheProcess()
    {
        Assert.Equal("beta", RunningBuild());
        return Task.CompletedTask;
    }
}
