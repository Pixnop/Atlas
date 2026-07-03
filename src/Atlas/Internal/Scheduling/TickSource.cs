namespace Atlas.Internal.Scheduling;

using Atlas.Api;

/// <summary>Tick-driven waits. Single-thread-confined to the game thread; no locking.</summary>
internal sealed class TickSource
{
    private readonly List<Waiter> _waiters = new();
    private int _tickCount;

    /// <summary>Gets the number of ticks raised so far.</summary>
    /// <remarks>Written on the game thread by <see cref="RaiseTick"/>; read through
    /// <see cref="System.Threading.Volatile"/> so cross-thread readers (e.g. the watchdog, polling
    /// from the thread pool) never observe a torn value. A reader may still observe a stale count if
    /// it races the very next tick; that staleness is acceptable because the value is only ever used
    /// for diagnostics (timeout error messages), not for control flow.</remarks>
    public int TickCount => Volatile.Read(ref _tickCount);

    /// <summary>Raises a tick, processing all pending waiters and completing those that are done.</summary>
    /// <remarks>Runs on the game thread (bridge tick listener).</remarks>
    public void RaiseTick()
    {
        Volatile.Write(ref _tickCount, _tickCount + 1);
        for (int i = _waiters.Count - 1; i >= 0; i--)
        {
            Waiter waiter = _waiters[i];
            Exception? error = waiter.OnTick(1);
            if (error != null)
            {
                _waiters.RemoveAt(i);
                waiter.Tcs.TrySetException(error);
            }
            else if (waiter.IsDone())
            {
                _waiters.RemoveAt(i);
                waiter.Tcs.TrySetResult();
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
    /// <param name="predicate">The predicate to poll.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait.</param>
    /// <returns>A task that completes when the predicate is true or timeout expires.</returns>
    public Task WaitUntilAsync(Func<bool> predicate, int timeoutTicks)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutTicks, 1);
        int elapsed = 0;
        return Register(
            isDone: predicate,
            onTick: _ =>
            {
                elapsed++;
                return elapsed >= timeoutTicks && !predicate()
                    ? new ScenarioTimeoutException($"Until predicate still false after {elapsed} ticks", elapsed)
                    : null;
            });
    }

    /// <summary>Registers a new waiter with the tick source.</summary>
    /// <param name="isDone">The completion predicate.</param>
    /// <param name="onTick">The per-tick callback, returns an exception to fail the wait or null to continue.</param>
    /// <returns>A task representing the wait.</returns>
    private Task Register(Func<bool> isDone, Func<int, Exception?> onTick)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Add(new Waiter(isDone, onTick, tcs));
        return tcs.Task;
    }

    /// <summary>Waiter record for tracking pending wait operations.</summary>
    private sealed record Waiter(Func<bool> IsDone, Func<int, Exception?> OnTick, TaskCompletionSource Tcs);
}
