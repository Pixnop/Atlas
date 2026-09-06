using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class WorldIsolationResolverTests
{
    private const string DisplayName = "MyScenarios.Scenario_Should_DoSomething";

    [Fact]
    public void Resolve_Should_ReturnSharedWorld_When_NoIsolationIsRequested()
    {
        WorldIsolation isolation = Resolve();

        Assert.Equal(WorldIsolation.SharedWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnFreshWorld_When_OnlyFreshWorldIsSet()
    {
        WorldIsolation isolation = Resolve(freshWorld: true);

        Assert.Equal(WorldIsolation.FreshWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_OnlyRollbackWorldIsSet()
    {
        WorldIsolation isolation = Resolve(rollbackWorld: true);

        Assert.Equal(WorldIsolation.RollbackWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRestartWorld_When_OnlyRestartWorldIsSet()
    {
        WorldIsolation isolation = Resolve(restartWorld: true);

        Assert.Equal(WorldIsolation.RestartWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_StrictIsolationAccompaniesRollbackWorld()
    {
        WorldIsolation isolation = Resolve(rollbackWorld: true, strictIsolation: true);

        Assert.Equal(WorldIsolation.RollbackWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_FreshWorldAndRollbackWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(freshWorld: true, rollbackWorld: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_FreshWorldAndRestartWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(freshWorld: true, restartWorld: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_RollbackWorldAndRestartWorldAreBothSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(rollbackWorld: true, restartWorld: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_AllThreeWorldFlagsAreSet()
    {
        Assert.Throws<AtlasSetupException>(
            () => Resolve(freshWorld: true, rollbackWorld: true, restartWorld: true));
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_StrictIsolationAccompaniesRestartWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(restartWorld: true, strictIsolation: true));

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
            () => Resolve(freshWorld, strictIsolation: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("StrictIsolation", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
    }

    /// <summary>Calls the resolver the way the invoker does, with the display name pinned and the
    /// scenario's flags spelled out one by one.</summary>
    private static WorldIsolation Resolve(
        bool freshWorld = false, bool rollbackWorld = false, bool restartWorld = false, bool strictIsolation = false) =>
        WorldIsolationResolver.Resolve(
            DisplayName, new ScenarioSettings(freshWorld, rollbackWorld, restartWorld, strictIsolation, 0));
}
