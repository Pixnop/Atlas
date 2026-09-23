using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace Sample.Scenarios;

/// <summary>The pattern from the bug report this feature answers: a mod author boots their mod
/// to check its assets parse, but a failed load used to only ever reach server-main.log, never
/// the test result. <see cref="IWorldSession.BootDiagnostics"/> makes it assertable without
/// changing what SampleMod itself ships (it boots clean, so the list is empty here); a mod with a
/// broken asset would show up as an <c>EnumLogType.Error</c> entry naming it.</summary>
[Trait("Category", "E2E")]
public class BootDiagnosticsScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public Task SampleMod_Should_LoadWithNoErrorOrAbove_When_ItsAssetsAreWellFormed()
    {
        Assert.DoesNotContain(World.BootDiagnostics, entry => entry.Level is EnumLogType.Error or EnumLogType.Fatal);
        return Task.CompletedTask;
    }
}
