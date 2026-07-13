using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

/// <summary>Pins the contract that a theory row obeys exactly the fact-style isolation rules:
/// the flags declared on <see cref="AtlasTheoryAttribute"/> flow through the same pure
/// <see cref="WorldIsolationResolver"/> as <c>[AtlasScenario]</c>'s (the resolver takes plain
/// flags and never reflects on either attribute), so the mutual exclusions and the strictness
/// rules cannot drift between facts and theory rows.</summary>
public class AtlasTheoryIsolationRulesTests
{
    private const string RowDisplayName = "MyScenarios.Theory_Should_DoSomething(row: 2)";

    [Fact]
    public void Resolve_Should_ReturnSharedWorld_When_TheoryRequestsNoIsolation()
    {
        Assert.Equal(WorldIsolation.SharedWorld, Resolve(new AtlasTheoryAttribute()));
    }

    [Fact]
    public void Resolve_Should_ReturnRestartWorld_When_TheoryRequestsRestartWorld()
    {
        Assert.Equal(WorldIsolation.RestartWorld, Resolve(new AtlasTheoryAttribute { RestartWorld = true }));
    }

    [Fact]
    public void Resolve_Should_ReturnRollbackWorld_When_TheoryRequestsStrictRollback()
    {
        Assert.Equal(
            WorldIsolation.RollbackWorld,
            Resolve(new AtlasTheoryAttribute { RollbackWorld = true, StrictIsolation = true }));
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_TheoryCombinesFreshWorldAndRollbackWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(new AtlasTheoryAttribute { FreshWorld = true, RollbackWorld = true }));

        Assert.Contains(RowDisplayName, ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_TheoryCombinesRollbackWorldAndRestartWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(new AtlasTheoryAttribute { RollbackWorld = true, RestartWorld = true }));

        Assert.Contains("RollbackWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_TheoryCombinesFreshWorldAndRestartWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(new AtlasTheoryAttribute { FreshWorld = true, RestartWorld = true }));

        Assert.Contains("FreshWorld", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
        Assert.Contains("contradict", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_TheorySetsStrictIsolationWithRestartWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(new AtlasTheoryAttribute { RestartWorld = true, StrictIsolation = true }));

        Assert.Contains("StrictIsolation", ex.Message);
        Assert.Contains("RestartWorld", ex.Message);
    }

    [Fact]
    public void Resolve_Should_ThrowSetupException_When_TheorySetsStrictIsolationWithoutRollbackWorld()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => Resolve(new AtlasTheoryAttribute { StrictIsolation = true }));

        Assert.Contains("StrictIsolation", ex.Message);
        Assert.Contains("RollbackWorld", ex.Message);
    }

    /// <summary>Feeds the attribute's flags to the resolver exactly the way the discoverer and
    /// runner chain do for one data row.</summary>
    private static WorldIsolation Resolve(AtlasTheoryAttribute attribute) =>
        WorldIsolationResolver.Resolve(
            RowDisplayName,
            attribute.FreshWorld,
            attribute.RollbackWorld,
            attribute.RestartWorld,
            attribute.StrictIsolation);
}
