using System.Reflection;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class SimulationTickSignalTests
{
    [Fact]
    public void FindEntitySimulationSystem_Should_PickTheMatchingEntry_When_TheArrayMatchesTheEngineShape()
    {
        // The engine's Systems array holds ~30 systems; the entity simulation is one entry
        // among unrelated ones, matched by its exact engine type name.
        object expected = new ServerSystemEntitySimulation();
        object?[] systems = [new OtherSystem(), expected, new OtherSystem()];

        Assert.Same(expected, SimulationTickSignal.FindEntitySimulationSystem(systems));
    }

    [Fact]
    public void FindEntitySimulationSystem_Should_MatchThroughBaseTypes_When_AForkSubclassesTheSystem()
    {
        // A fork that swaps in a subclass (instead of patching the class in place, like
        // Stratum does) must still be recognized: the walk climbs the base-type chain.
        object subclassed = new ForkedEntitySimulation();

        Assert.Same(subclassed, SimulationTickSignal.FindEntitySimulationSystem(new[] { subclassed }));
    }

    [Fact]
    public void FindEntitySimulationSystem_Should_ReturnNull_When_TheArrayItselfIsGone()
        => Assert.Null(SimulationTickSignal.FindEntitySimulationSystem(null));

    [Fact]
    public void FindEntitySimulationSystem_Should_ReturnNull_When_NoEntryMatches()
        => Assert.Null(SimulationTickSignal.FindEntitySimulationSystem(
            new object?[] { new OtherSystem(), null }));

    [Fact]
    public void ResolveStampField_Should_FindInheritedStamp_When_DeclaredOnABase()
    {
        // The engine declares millisecondsSinceStart on the ServerSystem base, not on
        // ServerSystemEntitySimulation itself; a public inherited field must still resolve.
        FieldInfo? field = SimulationTickSignal.ResolveStampField(typeof(ServerSystemEntitySimulation));

        Assert.NotNull(field);
        Assert.Equal(typeof(SystemBase), field.DeclaringType);
    }

    [Fact]
    public void ResolveStampField_Should_ReturnNull_When_TheStampFieldIsMissing()
        => Assert.Null(SimulationTickSignal.ResolveStampField(typeof(OtherSystem)));

    [Fact]
    public void ResolveStampField_Should_ReturnNull_When_TheStampIsNoLongerALong()
        => Assert.Null(SimulationTickSignal.ResolveStampField(typeof(IntStampSystem)));

    [Fact]
    public void ResolveStampField_Should_Throw_When_TheSystemTypeIsNull()
        => Assert.Throws<ArgumentNullException>(() => SimulationTickSignal.ResolveStampField(null!));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 33, true)]
    [InlineData(33, 33, false)]
    [InlineData(33, 66, true)]
    public void HasTicked_Should_DetectAFire_When_TheStampMoved(
        long lastStamp, long currentStamp, bool expected)
        => Assert.Equal(expected, SimulationTickSignal.HasTicked(lastStamp, currentStamp));

    [Fact]
    public void DescribeUnavailable_Should_NameTheDriftedSymbolsAndTheGameVersion()
    {
        string message = SimulationTickSignal.DescribeUnavailable("9.99.9");

        Assert.Contains("EntitySimulationTicks", message);
        Assert.Contains("ServerMain.Systems", message);
        Assert.Contains("ServerSystemEntitySimulation", message);
        Assert.Contains("millisecondsSinceStart", message);
        Assert.Contains("9.99.9", message);
        Assert.Contains("drifted", message);
    }

#pragma warning disable CS0649 // Fields are resolved and read through reflection only.
#pragma warning disable SA1307, SA1401, S1144, S2933, CA1051, CA1823 // Fields deliberately mirror
    // the engine's shape: public lower-case millisecondsSinceStart on the ServerSystem base.
    private class SystemBase
    {
        public long millisecondsSinceStart;
    }

    private class ServerSystemEntitySimulation : SystemBase
    {
    }

    private sealed class ForkedEntitySimulation : ServerSystemEntitySimulation
    {
    }

    private sealed class OtherSystem
    {
    }

    private sealed class IntStampSystem
    {
        public int millisecondsSinceStart;
    }
#pragma warning restore CS0649
#pragma warning restore SA1307, SA1401, S1144, S2933, CA1051, CA1823
}
