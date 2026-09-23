namespace Atlas.Pure.Tests.Scheduling;

using Atlas.Api;
using Atlas.Internal.Scheduling;

public class TickSourceTests
{
    [Fact]
    public void WaitTicksAsync_Should_Complete_When_ExactTickCountRaised()
    {
        var source = new TickSource();
        Task wait = source.WaitTicksAsync(3);
        source.RaiseTick();
        source.RaiseTick();
        Assert.False(wait.IsCompleted);
        source.RaiseTick();
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void WaitUntilAsync_Should_Complete_When_PredicateTurnsTrue()
    {
        var source = new TickSource();
        bool flag = false;
        Task wait = source.WaitUntilAsync(() => flag, timeoutTicks: 10);
        source.RaiseTick();
        Assert.False(wait.IsCompleted);
        flag = true;
        source.RaiseTick();
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void WaitUntilAsync_Should_ThrowScenarioTimeout_When_TimeoutTicksExceeded()
    {
        var source = new TickSource();
        Task wait = source.WaitUntilAsync(() => false, timeoutTicks: 2);
        source.RaiseTick();
        source.RaiseTick();
        ScenarioTimeoutException ex = Assert.IsType<ScenarioTimeoutException>(wait.Exception!.InnerException);
        Assert.Equal(2, ex.TicksWaited);
        Assert.Equal("Until predicate still false after 2 ticks", ex.Message);
    }

    [Fact]
    public void WaitTicksAsync_Should_Throw_When_TicksIsLessThanOne()
    {
        var source = new TickSource();
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => { source.WaitTicksAsync(0); }));
    }

    [Fact]
    public void WaitUntilAsync_Should_Throw_When_PredicateIsNull()
    {
        var source = new TickSource();
        Assert.IsType<ArgumentNullException>(
            Record.Exception(() => { source.WaitUntilAsync(null!, timeoutTicks: 5); }));
    }

    [Fact]
    public void WaitUntilAsync_Should_Throw_When_TimeoutTicksIsLessThanOne()
    {
        var source = new TickSource();
        Assert.IsType<ArgumentOutOfRangeException>(
            Record.Exception(() => { source.WaitUntilAsync(() => true, timeoutTicks: 0); }));
    }

    [Fact]
    public void RaiseTick_Should_StopServingAWaiter_When_ItAlreadyCompleted()
    {
        var source = new TickSource();
        int predicateCalls = 0;
        Task wait = source.WaitUntilAsync(
            () =>
            {
                predicateCalls++;
                return true;
            },
            timeoutTicks: 10);

        source.RaiseTick();
        Assert.True(wait.IsCompletedSuccessfully);
        int callsAtCompletion = predicateCalls;

        // A removed waiter is never consulted again; a leftover one would keep incrementing.
        source.RaiseTick();
        source.RaiseTick();
        Assert.Equal(callsAtCompletion, predicateCalls);
    }

    [Fact]
    public void WaitUntilAsync_Should_FaultWithPredicateException_When_PredicateThrows()
    {
        var source = new TickSource();
        var boom = new InvalidOperationException("predicate blew up");
        Task wait = source.WaitUntilAsync(() => throw boom, timeoutTicks: 10);

        source.RaiseTick();

        Assert.True(wait.IsFaulted);
        Assert.Same(boom, wait.Exception!.InnerException);
    }

    [Fact]
    public void RaiseTick_Should_KeepServingOtherWaiters_When_OnePredicateThrows()
    {
        var source = new TickSource();

        // Registration order matters: waiters are served newest first, so the thrower has to be
        // registered last to be the one processed first. Registered first it would be processed
        // last, and the healthy waiter would already have been served even without the fix.
        Task healthy = source.WaitTicksAsync(1);
        Task thrower = source.WaitUntilAsync(() => throw new InvalidOperationException("boom"), timeoutTicks: 10);

        source.RaiseTick();

        Assert.True(thrower.IsFaulted);
        Assert.True(healthy.IsCompletedSuccessfully);
    }

    [Fact]
    public void WaitTicksAsync_Should_ServeMultipleWaiters_When_Interleaved()
    {
        var source = new TickSource();
        Task a = source.WaitTicksAsync(1);
        Task b = source.WaitTicksAsync(2);
        source.RaiseTick();
        Assert.True(a.IsCompletedSuccessfully);
        Assert.False(b.IsCompleted);
        source.RaiseTick();
        Assert.True(b.IsCompletedSuccessfully);
    }

    [Fact]
    public void TickCount_Should_Increment_When_RaiseTickIsCalled()
    {
        var source = new TickSource();
        source.RaiseTick();
        source.RaiseTick();
        Assert.Equal(2, source.TickCount);
    }

    [Fact]
    public void FailAll_Should_FaultPendingWaiter_When_Called()
    {
        var source = new TickSource();
        Task wait = source.WaitTicksAsync(3);
        var exception = new InvalidOperationException("boom");
        source.FailAll(exception);
        Assert.True(wait.IsFaulted);
        Assert.Same(exception, wait.Exception!.InnerException);
    }

    [Fact]
    public void FailAll_Should_ClearWaiters_So_ALaterTickNeverServesThem()
    {
        var source = new TickSource();
        int predicateCalls = 0;
        source.WaitUntilAsync(
            () =>
            {
                predicateCalls++;
                return false;
            },
            timeoutTicks: 100);

        source.FailAll(new InvalidOperationException("boom"));
        source.RaiseTick();

        // A cleared list has nothing left to consult; a leftover waiter would still be polled.
        Assert.Equal(0, predicateCalls);
    }
}
