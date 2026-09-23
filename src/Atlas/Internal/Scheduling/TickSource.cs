namespace Atlas.Internal.Scheduling;

using Atlas.Api;

/// <summary>Tick-driven waits.</summary>
/// <remarks>Bound to whichever thread constructs it (<see cref="_ownerThreadId"/>): that thread
/// is meant to be the game thread, ServerHost's own <c>GameThreadMain</c> constructing this
/// instance on the freshly started thread, before subscribing it to the bridge's tick event.
/// That binding matters because <c>ServerHost.DisposeAsync</c> bounds its game-thread join and abandons a wedged thread
/// rather than blocking forever (issue #8), and every host's bridge mod reaches a
/// <see cref="TickSource"/> through the same static <c>BridgeRendezvous.NotifyTick</c>. An
/// abandoned thread that is still ticking its own (disposed) host can therefore overlap a
/// freshly booted host's game thread for a brief window, both able to call
/// <see cref="RaiseTick"/> on the SAME instance: the freshly booted one, since the static
/// delegate the abandoned thread holds was re-pointed at it by <c>BridgeRendezvous.Reset</c>.
/// <see cref="RaiseTick"/> ignores any call whose thread does not match <see cref="_ownerThreadId"/>,
/// so the abandoned thread's ticks are dropped outright rather than merely serialized: they
/// never advance <see cref="TickCount"/> or poll a waiter. <see cref="_gate"/> still guards
/// <see cref="_waiters"/> against <see cref="Register"/> (via <see cref="WaitTicksAsync"/>/
/// <see cref="WaitUntilAsync"/>) running concurrently with <see cref="RaiseTick"/> from the
/// owning thread, since callers register from whichever thread awaits them. A waiter's callback
/// runs while <see cref="_gate"/> is held: no callback in this codebase marshals synchronously
/// onto another thread that is itself blocked in <see cref="Register"/> or <see cref="RaiseTick"/>
/// on the same instance, and caller predicates are documented to run on the game thread, but one
/// that did would deadlock against this lock.</remarks>
internal sealed class TickSource
{
    private readonly object _gate = new();
    private readonly List<Waiter> _waiters = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _tickCount;

    /// <summary>Gets the number of ticks raised so far.</summary>
    /// <remarks>Written on the game thread by <see cref="RaiseTick"/>; read through
    /// <see cref="System.Threading.Volatile"/> so cross-thread readers (e.g. the watchdog, polling
    /// from the thread pool) never observe a torn value. A reader may still observe a stale count if
    /// it races the very next tick; that staleness is acceptable because the value is only ever used
    /// for diagnostics (timeout error messages), not for control flow.</remarks>
    public int TickCount => Volatile.Read(ref _tickCount);

    /// <summary>Raises a tick, processing all pending waiters and completing those that are done.</summary>
    /// <remarks>Called from the game thread (bridge tick listener). A call from any thread other
    /// than the one that constructed this instance is ignored: see the class remarks for why an
    /// abandoned game thread can still reach this method, and why dropping its ticks rather than
    /// merely serializing them is the fix. A waiter callback that throws faults that one waiter
    /// and no other: the callbacks are caller-supplied predicates (an <c>Until</c> predicate
    /// dereferencing a player the server just dropped is the everyday case), and the bridge
    /// registers this listener with no error handler, so an escaping exception is caught and
    /// merely logged by <c>ServerMain.Process</c>, leaving the waiter both unremoved and
    /// uncompleted: the scenario would hang to the watchdog and report a timeout instead of the
    /// predicate's own exception, and every waiter below this one in the list would be skipped
    /// for as long as the throw repeats. Each waiter's callback runs at most once per call: a
    /// caller-supplied predicate may itself throw or carry side effects (a counter, a retry
    /// budget), so it must be polled exactly as many times as the contract promises, not once
    /// more because two different code paths both wanted an answer.</remarks>
    public void RaiseTick()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            return;
        }

        lock (_gate)
        {
            Volatile.Write(ref _tickCount, _tickCount + 1);
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                Waiter waiter = _waiters[i];
                Exception? error;
                try
                {
                    error = waiter.OnTick(1);
                    if (error == null && !waiter.IsDone())
                    {
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // Deliberately broad: whatever a caller's predicate throws belongs to that
                    // caller's await, not to the game thread. Only the two caller callbacks run
                    // inside the try, so the removal below happens exactly once per waiter.
                    error = ex;
                }

                _waiters.RemoveAt(i);
                if (error == null)
                {
                    waiter.Tcs.TrySetResult();
                }
                else
                {
                    waiter.Tcs.TrySetException(error);
                }
            }
        }
    }

    /// <summary>Waits for a specified number of ticks to elapse.</summary>
    /// <param name="ticks">The number of ticks to wait.</param>
    /// <returns>A task that completes when the ticks have elapsed.</returns>
    public Task WaitTicksAsync(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        int remaining = ticks;
        return Register(isDone: () => remaining == 0, onTick: _ =>
        {
            remaining--;
            return null;
        });
    }

    /// <summary>Waits until a predicate becomes true or timeout is reached.</summary>
    /// <param name="predicate">The predicate to poll. Polled at most once per tick: <see cref="RaiseTick"/>
    /// calls <c>onTick</c> and then, only when that leaves the wait undecided, its own
    /// <c>isDone</c> re-check, so this method must decide completion from a single call to
    /// <paramref name="predicate"/> and hand both callbacks the same answer instead of letting
    /// each call it independently.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait.</param>
    /// <returns>A task that completes when the predicate is true or timeout expires.</returns>
    public Task WaitUntilAsync(Func<bool> predicate, int timeoutTicks)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutTicks, 1);
        int elapsed = 0;
        bool done = false;
        return Register(
            isDone: () => done,
            onTick: _ =>
            {
                elapsed++;
                done = predicate();
                return !done && elapsed >= timeoutTicks
                    ? new ScenarioTimeoutException($"Until predicate still false after {elapsed} ticks", elapsed)
                    : null;
            });
    }

    /// <summary>Faults every pending waiter with the given exception and clears the list.</summary>
    /// <param name="exception">The exception to fault all pending waiters with.</param>
    /// <remarks>Called from the game thread; see the class remarks for why <see cref="_gate"/>
    /// guards it anyway.</remarks>
    internal void FailAll(Exception exception)
    {
        lock (_gate)
        {
            foreach (Waiter waiter in _waiters)
            {
                waiter.Tcs.TrySetException(exception);
            }

            _waiters.Clear();
        }
    }

    /// <summary>Registers a new waiter with the tick source.</summary>
    /// <param name="isDone">The completion predicate.</param>
    /// <param name="onTick">The per-tick callback, returns an exception to fail the wait or null to continue.</param>
    /// <returns>A task representing the wait.</returns>
    private Task Register(Func<bool> isDone, Func<int, Exception?> onTick)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiters.Add(new Waiter(isDone, onTick, tcs));
        }

        return tcs.Task;
    }

    /// <summary>Waiter record for tracking pending wait operations.</summary>
    private sealed record Waiter(Func<bool> IsDone, Func<int, Exception?> OnTick, TaskCompletionSource Tcs);
}
