using NSubstitute;

namespace Atlas.Pure.Tests.Api;

/// <summary>Covers <c>Until(predicate, description, timeoutTicks)</c>, the extension that adds a
/// description to <see cref="IWorldSession.Until"/> without a member on the interface. The session
/// is a substitute whose own <c>Until</c> is backed by a real <see cref="TickSource"/>, so the
/// interface's timeout message is the one the engine run produces, and the overload tests below
/// check which of the two methods a call site reaches.</summary>
public class WorldSessionExtensionsTests
{
    private const string PlainTimeout = "Until predicate still false after 3 ticks (timeoutTicks is 3; pass a larger value to wait longer)";

    [Fact]
    public async Task Until_Should_CompleteLikeTheInterfacesUntil_When_ThePredicateTurnsTrue()
    {
        (IWorldSession world, TickSource ticks) = NewSession();
        bool flag = false;

        Task wait = world.Until(() => flag, "the flag is set", timeoutTicks: 5);
        ticks.RaiseTick();
        Assert.False(wait.IsCompleted);
        flag = true;
        ticks.RaiseTick();

        await wait;
    }

    [Fact]
    public async Task Until_Should_PassThePredicateAndTheBoundToTheInterface_When_Called()
    {
        (IWorldSession world, _) = NewSession();
        Func<bool> predicate = () => true;
        world.Until(Arg.Any<Func<bool>>(), Arg.Any<int>()).Returns(Task.CompletedTask);

        await world.Until(predicate, "always", timeoutTicks: 7);

        _ = world.Received(1).Until(predicate, 7);
    }

    [Fact]
    public async Task Until_Should_UseTheInterfacesDefaultBound_When_NoBoundIsGiven()
    {
        (IWorldSession world, _) = NewSession();
        Func<bool> predicate = () => true;
        world.Until(Arg.Any<Func<bool>>(), Arg.Any<int>()).Returns(Task.CompletedTask);

        await world.Until(predicate, "always");

        // 600 is the documented default of IWorldSession.Until.
        _ = world.Received(1).Until(predicate, 600);
    }

    [Fact]
    public async Task Until_Should_CarryTheDescriptionTheBoundAndTheTicks_When_TheWaitTimesOut()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        Task wait = world.Until(() => false, "the lever is down", timeoutTicks: 3);
        ticks.RaiseTick();
        ticks.RaiseTick();
        ticks.RaiseTick();

        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(() => wait);
        Assert.Equal(3, ex.TicksWaited);
        Assert.Equal(
            "Until predicate \"the lever is down\" still false after 3 ticks (timeoutTicks is 3; pass a larger value to wait longer)",
            ex.Message);
    }

    [Fact]
    public async Task Until_Should_SayOneTickInTheSingular_When_ASingleTickTimedOut()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        Task wait = world.Until(() => false, "the lever is down", timeoutTicks: 1);
        ticks.RaiseTick();

        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(() => wait);
        Assert.Equal(1, ex.TicksWaited);
        Assert.Equal(
            "Until predicate \"the lever is down\" still false after 1 tick (timeoutTicks is 1; pass a larger value to wait longer)",
            ex.Message);
    }

    [Fact]
    public async Task Until_Should_LeaveATimeoutFromInsideThePredicateAlone_When_ItIsNotTheWaitsOwn()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        // A predicate that itself runs a bounded wait and gives up: that timeout belongs to the
        // wait it came from, not to this Until's bound, so the description does not apply to it.
        Task wait = world.Until(() => throw new ScenarioTimeoutException("inner wait gave up", 99), "the lever is down", timeoutTicks: 3);
        ticks.RaiseTick();

        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(() => wait);
        Assert.Equal("inner wait gave up", ex.Message);
        Assert.Equal(99, ex.TicksWaited);
    }

    [Fact]
    public async Task Until_Should_LetAnyOtherPredicateExceptionThrough_When_ThePredicateThrows()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        Task wait = world.Until(() => throw new InvalidOperationException("no such player"), "the player is back", timeoutTicks: 3);
        ticks.RaiseTick();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        Assert.Equal("no such player", ex.Message);
    }

    [Fact]
    public void Until_Should_ThrowSynchronously_When_AnArgumentIsWrong()
    {
        (IWorldSession world, _) = NewSession();
        Func<bool> predicate = () => true;

        // The interface's Until validates before it returns a task; so does this one.
        Assert.Throws<ArgumentNullException>(() => { _ = world.Until(null!, "x"); });
        Assert.Throws<ArgumentNullException>(() => { _ = world.Until(predicate, null!); });
        Assert.Throws<ArgumentException>(() => { _ = world.Until(predicate, string.Empty); });
        Assert.Throws<ArgumentException>(() => { _ = world.Until(predicate, "  "); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = world.Until(predicate, "x", timeoutTicks: 0); });
        Assert.Throws<ArgumentNullException>(() => { _ = WorldSessionExtensions.Until(null!, predicate, "x"); });
    }

    [Fact]
    public async Task Until_Should_ReachTheInterfacesOwnMethod_When_TheSecondArgumentIsATickCount()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        // Both forms keep the interface's wording: no description, so the plain message.
        Task positional = world.Until(() => false, 3);
        Task named = world.Until(() => false, timeoutTicks: 3);
        Task defaulted = world.Until(() => true);
        ticks.RaiseTick();
        ticks.RaiseTick();
        ticks.RaiseTick();

        Assert.Equal(PlainTimeout, (await Assert.ThrowsAsync<ScenarioTimeoutException>(() => positional)).Message);
        Assert.Equal(PlainTimeout, (await Assert.ThrowsAsync<ScenarioTimeoutException>(() => named)).Message);
        await defaulted;
    }

    [Fact]
    public async Task Until_Should_ReachTheExtension_When_TheSecondArgumentIsADescription()
    {
        (IWorldSession world, TickSource ticks) = NewSession();

        // Positional, named, and with the bound named too: three spellings, one method.
        Task positional = world.Until(() => false, "the lever is down", 3);
        Task named = world.Until(() => false, description: "the lever is down");
        Task reordered = world.Until(() => false, timeoutTicks: 3, description: "the lever is down");
        ticks.RaiseTick();
        ticks.RaiseTick();
        ticks.RaiseTick();

        Assert.Contains("\"the lever is down\"", (await Assert.ThrowsAsync<ScenarioTimeoutException>(() => positional)).Message);
        Assert.Contains("\"the lever is down\"", (await Assert.ThrowsAsync<ScenarioTimeoutException>(() => reordered)).Message);

        // The named-only call has the default bound, 600 ticks: still waiting after three.
        Assert.False(named.IsCompleted);
    }

    private static (IWorldSession World, TickSource Ticks) NewSession()
    {
        var ticks = new TickSource();
        IWorldSession world = Substitute.For<IWorldSession>();
        world.Until(Arg.Any<Func<bool>>(), Arg.Any<int>())
            .Returns(call => ticks.WaitUntilAsync(call.Arg<Func<bool>>(), call.Arg<int>(), callerBound: true));
        return (world, ticks);
    }
}
