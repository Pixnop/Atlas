using Xunit.Runners;

namespace Atlas.Cli;

/// <summary>Disposal hygiene for the in-process xunit <see cref="AssemblyRunner"/> (issue #59).
/// xunit.runner.utility 2.x has a documented disposal race: <c>Dispose()</c> closes the runner's
/// completion events while the runner's own worker thread may still be about to wait on one of
/// them, and the worker's <c>WaitOne()</c> on the closed handle then throws
/// <see cref="ObjectDisposedException"/> on a pool thread, which kills the whole process. The
/// mitigation is to dispose only once <see cref="AssemblyRunner.Status"/> has settled to
/// <see cref="AssemblyRunnerStatus.Idle"/> (bounded wait plus a short grace), and to prefer
/// LEAKING the runner over disposing it hot: a leaked event in a finishing process is harmless,
/// a disposed-while-awaited event is a process kill.</summary>
/// <remarks>Verified against the decompiled 2.9.3 sources: Idle proves the worker has passed
/// every discovery wait (the worker itself sets the discovery-complete event after them), but
/// NOT that it has entered its final <c>executionCompleteEvent.WaitOne()</c>
/// (AssemblyRunner.cs:263, the crash site): that event is set by the message-sink thread while
/// the worker may still be inside <c>RunTests</c>, so <see cref="DisposeGrace"/> gives the
/// worker time to reach and clear the wait before the handle goes away. A runner that never
/// idles is also real upstream: a cancelled run never sets the execution-complete event.</remarks>
internal static class RunnerDisposal
{
    /// <summary>How long to wait for the runner to reach Idle before giving up and leaking it.</summary>
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Poll step between two <see cref="AssemblyRunner.Status"/> reads.</summary>
    internal static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(10);

    /// <summary>Grace between observing Idle and disposing, covering the tail window where the
    /// worker has not yet entered its final wait (Idle cannot regress: the events are only ever
    /// reset by <c>Start</c>, which is never called again on a finished runner).</summary>
    internal static readonly TimeSpan DisposeGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>Disposes <paramref name="runner"/> once it reports Idle, or leaks it after
    /// <see cref="IdleTimeout"/>: never disposes a runner that still reports itself busy.</summary>
    /// <param name="runner">The finished (or abandoned) runner to dispose.</param>
    /// <returns>True when the runner idled and was disposed; false when it was leaked.</returns>
    public static bool DisposeWhenIdle(AssemblyRunner runner) =>
        DisposeWhenIdle(() => runner.Status == AssemblyRunnerStatus.Idle, runner.Dispose, Thread.Sleep);

    /// <summary>Pure core of <see cref="DisposeWhenIdle(AssemblyRunner)"/>: polls
    /// <paramref name="isIdle"/> every <see cref="PollStep"/> for at most
    /// <see cref="IdleTimeout"/>, then sleeps <see cref="DisposeGrace"/> and calls
    /// <paramref name="dispose"/>; gives up without disposing when the runner never idles.</summary>
    /// <param name="isIdle">Reads whether the runner currently reports Idle.</param>
    /// <param name="dispose">Disposes the runner; called at most once, only after idling.</param>
    /// <param name="sleep">Sleeps for the given duration (injectable for tests).</param>
    /// <returns>True when the runner idled and was disposed; false when it was leaked.</returns>
    internal static bool DisposeWhenIdle(Func<bool> isIdle, Action dispose, Action<TimeSpan> sleep)
    {
        long remainingPolls = IdleTimeout.Ticks / PollStep.Ticks;
        while (!isIdle())
        {
            if (remainingPolls-- <= 0)
            {
                return false;
            }

            sleep(PollStep);
        }

        sleep(DisposeGrace);
        dispose();
        return true;
    }
}
