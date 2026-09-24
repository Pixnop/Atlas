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
    public void WaitUntilAsync_Should_PollPredicateExactlyOnce_When_ItTurnsTrueOnTheTimeoutTick()
    {
        // RaiseTick used to consult a WaitUntilAsync predicate through two separate paths on the
        // same tick: onTick's own timeout check (elapsed >= timeoutTicks && !predicate()) and the
        // isDone() re-check RaiseTick runs right after. Both fire on the exact tick where the
        // predicate turns true at the timeout boundary, so a side-effecting predicate (a counter,
        // or one that throws) gets polled twice for what the contract promises is one tick's
        // worth of work.
        var source = new TickSource();
        int calls = 0;
        bool flag = true;
        Task wait = source.WaitUntilAsync(
            () =>
            {
                calls++;
                return flag;
            },
            timeoutTicks: 1);

        source.RaiseTick();

        Assert.True(wait.IsCompletedSuccessfully);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WaitUntilAsync_Should_PollPredicateExactlyOnce_When_ItThrowsOnTheTimeoutTick()
    {
        var source = new TickSource();
        int calls = 0;
        var boom = new InvalidOperationException("predicate blew up exactly at the deadline");
        Task wait = source.WaitUntilAsync(
            () =>
            {
                calls++;
                throw boom;
            },
            timeoutTicks: 1);

        source.RaiseTick();

        Assert.True(wait.IsFaulted);
        Assert.Same(boom, wait.Exception!.InnerException);
        Assert.Equal(1, calls);
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
    public void RaiseTick_Should_BeIgnored_When_CalledFromANonOwningThread()
    {
        var source = new TickSource();
        Task ticksWait = source.WaitTicksAsync(1);
        int predicateCalls = 0;
        Task untilWait = source.WaitUntilAsync(
            () =>
            {
                predicateCalls++;
                return true;
            },
            timeoutTicks: 10);

        // A dedicated thread rather than Task.Run: the pool may run the work item on the very
        // thread that constructed the source, which would then pass the owner check.
        var foreign = new Thread(source.RaiseTick) { IsBackground = true };
        foreign.Start();
        foreign.Join();

        Assert.Equal(0, source.TickCount);
        Assert.False(ticksWait.IsCompleted);
        Assert.False(untilWait.IsCompleted);
        Assert.Equal(0, predicateCalls);
    }

    [Fact]
    public void RaiseTick_Should_IgnoreTheAbandonedThread_When_TwoGameThreadsOverlap()
    {
        // TickSource now binds to the thread that constructs it (see class remarks): RaiseTick
        // from any other thread is a no-op. ServerHost.DisposeAsync bounds the game-thread join
        // and, if it times out, abandons that thread rather than blocking forever (the issue #8
        // hazard). An abandoned thread's embedded server keeps ticking, and every host's bridge
        // mod calls the same static BridgeRendezvous.NotifyTick, so a still-ticking abandoned
        // thread and a freshly booted host's game thread can both end up calling RaiseTick() on
        // the SAME TickSource for a brief window. Reproduced directly here without the embedded
        // server: the source is constructed on the "owner" thread, and only that thread's ticks
        // may ever poll the predicate, however the two threads interleave. Both sides run on
        // dedicated threads rather than Task.Run: a pool thread that finishes the owner's work
        // is free to pick up the abandoned work item next and would then run it under the
        // owner's own managed thread id, passing the check for the wrong reason. The owner
        // thread also stays alive until the abandoned thread is done, since a managed thread id
        // can be reused once its thread exits; that also matches the real case, where the
        // abandoned game thread is still alive and ticking while the new owner thread runs.
        for (int attempt = 0; attempt < 200; attempt++)
        {
            using var start = new ManualResetEventSlim();
            using var ownerReady = new ManualResetEventSlim();
            using var abandonedDone = new ManualResetEventSlim();
            TickSource source = null!;
            Task wait = null!;
            int calls = 0;
            var boom = new InvalidOperationException("predicate blew up");

            var ownerThread = new Thread(() =>
            {
                source = new TickSource();
                wait = source.WaitUntilAsync(
                    () =>
                    {
                        Interlocked.Increment(ref calls);
                        throw boom;
                    },
                    timeoutTicks: 1000);
                ownerReady.Set();
                RaiseTicks(source, start);
                abandonedDone.Wait();
            })
            { IsBackground = true };
            ownerThread.Start();

            ownerReady.Wait();
            var abandonedThread = new Thread(() =>
            {
                RaiseTicks(source, start);
                abandonedDone.Set();
            })
            { IsBackground = true };
            abandonedThread.Start();

            start.Set();
            ownerThread.Join();
            abandonedThread.Join();

            Assert.Equal(50, source.TickCount); // only the owner thread's 50 ticks count
            Assert.True(calls <= 1, $"predicate called {calls} times on attempt {attempt}");
            Assert.True(wait.IsFaulted);
            Assert.Same(boom, wait.Exception!.InnerException);
        }

        static void RaiseTicks(TickSource source, ManualResetEventSlim start)
        {
            start.Wait();
            for (int i = 0; i < 50; i++)
            {
                source.RaiseTick();
            }
        }
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
