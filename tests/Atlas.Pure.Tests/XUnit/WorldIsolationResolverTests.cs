using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class WorldIsolationResolverTests
{
    private const string DisplayName = "MyScenarios.Scenario_Should_DoSomething";

    [Fact]
    public void Resolve_Should_ReturnSharedWorld_When_NoIsolationIsRequested()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(
            DisplayName, freshWorld: false, rollbackWorld: false, restartWorld: false, strictIsolation: false);

        Assert.Equal(WorldIsolation.SharedWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnFreshWorld_When_OnlyFreshWorldIsSet()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(
            DisplayName, freshWorld: true, rollbackWorld: false, restartWorld: false, strictIsolation: false);

        Assert.Equal(WorldIsolation.FreshWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_OnlyRollbackWorldIsSet()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(
            DisplayName, freshWorld: false, rollbackWorld: true, restartWorld: false, strictIsolation: false);

        Assert.Equal(WorldIsolation.RollbackWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRestartWorld_When_OnlyRestartWorldIsSet()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(
            DisplayName, freshWorld: false, rollbackWorld: false, restartWorld: true, strictIsolation: false);

        Assert.Equal(WorldIsolation.RestartWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_StrictIsolationAccompaniesRollbackWorld()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(
            DisplayName, freshWorld: false, rollbackWorld: true, restartWorld: false, strictIsolation: true);

        Assert.Equal(WorldIsolation.RollbackWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_FreshWorldAndRollbackWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld: true, rollbackWorld: true, restartWorld: false, strictIsolation: false));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_FreshWorldAndRestartWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld: true, rollbackWorld: false, restartWorld: true, strictIsolation: false));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_RollbackWorldAndRestartWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld: false, rollbackWorld: true, restartWorld: true, strictIsolation: false));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_AllThreeWorldFlagsAreSet()
    {
        Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld: true, rollbackWorld: true, restartWorld: true, strictIsolation: false));
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_StrictIsolationAccompaniesRestartWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld: false, rollbackWorld: false, restartWorld: true, strictIsolation: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("StrictIsolation", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("works or fails the scenario hard", ex.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_Should_ThrowSetupException_When_StrictIsolationLacksRollbackWorld(bool freshWorld)
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(
                DisplayName, freshWorld, rollbackWorld: false, restartWorld: false, strictIsolation: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("StrictIsolation", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
    }
}
