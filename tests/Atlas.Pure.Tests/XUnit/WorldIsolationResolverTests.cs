using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class WorldIsolationResolverTests
{
    private const string DisplayName = "MyScenarios.Scenario_Should_DoSomething";

    [Fact]
    public void Resolve_Should_ReturnSharedWorld_When_NoIsolationIsRequested()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(DisplayName, freshWorld: false, rollbackWorld: false);

        Assert.Equal(WorldIsolation.SharedWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnFreshWorld_When_OnlyFreshWorldIsSet()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(DisplayName, freshWorld: true, rollbackWorld: false);

        Assert.Equal(WorldIsolation.FreshWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_OnlyRollbackWorldIsSet()
    {
        WorldIsolation isolation = WorldIsolationResolver.Resolve(DisplayName, freshWorld: false, rollbackWorld: true);

        Assert.Equal(WorldIsolation.RollbackWorld, isolation);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_BothIsolationFlagsAreSet()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => WorldIsolationResolver.Resolve(DisplayName, freshWorld: true, rollbackWorld: true));

        Assert.Contains(DisplayName, ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }
}
