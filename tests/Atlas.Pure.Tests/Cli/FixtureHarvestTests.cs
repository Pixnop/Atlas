using System.Reflection;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

/// <summary>The collection serializes this class with HostRegistryConcurrencyTests: harvesting
/// enters the registry's process-wide gate, which that class deliberately holds busy.</summary>
[Collection("HostRegistry")]
public class FixtureHarvestTests
{
    [Fact]
    public void FindHarvestMethod_Should_ExplainTheMissingHarness_When_AtlasXUnitIsNotLoaded()
    {
        MethodInfo? method = FixtureHarvest.FindHarvestMethod(
            [typeof(object).Assembly], out string? error);

        Assert.Null(method);
        Assert.NotNull(error);
        Assert.Contains("Atlas.XUnit", error);
    }

    [Fact]
    public void FindHarvestMethod_Should_ResolveTheSeam_When_TheRealHarnessIsLoaded()
    {
        // This test project references the real Atlas.XUnit, so the reflection contract the CLI
        // pins by name (type, method, static Task-of-string shape) is verified against it here;
        // a rename on either side fails this test instead of a slow E2E run.
        Assembly harness = typeof(Atlas.XUnit.AtlasScenarioBase).Assembly;

        MethodInfo? method = FixtureHarvest.FindHarvestMethod([harness], out string? error);

        Assert.Null(error);
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Empty(method.GetParameters());
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    [Fact]
    public void ShutDownAndHarvestSavePath_Should_ReturnNullWithoutError_When_NoHostIsLive()
    {
        // Force the harness into the loaded-assembly list, then harvest: with no scenario ever
        // run in this process there is no live host, which must read as "nothing to harvest",
        // not as a failure.
        _ = typeof(Atlas.XUnit.AtlasScenarioBase).Assembly;

        string? savePath = FixtureHarvest.ShutDownAndHarvestSavePath(out string? error);

        Assert.Null(error);
        Assert.Null(savePath);
    }
}
