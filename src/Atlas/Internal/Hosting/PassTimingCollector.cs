using System.Runtime.CompilerServices;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Collects one server pass's busy time - <c>Process()</c> minus the engine's own
/// pacing sleep - for every pass sampled while an <see cref="Api.IWorldSession.MeasureTicks"/>
/// window is open. Windows are independent: two windows open at once (e.g. two overlapping
/// <see cref="Api.IWorldSession.MeasureTicks"/> calls on the same host, the way <c>Ticks</c> and
/// <c>Until</c> already allow concurrent waiters) each get their own list and both see every
/// pass sampled while they are open. Single-thread-confined to the game thread, like
/// <see cref="Scheduling.TickSource"/>: <see cref="Start"/> and <see cref="StopAndCollect"/> run
/// from a scenario continuation, <see cref="RecordPass"/> runs from the pump, and both are the
/// same thread by construction (the scheduler only ever drains on the thread the pump is looping
/// on), so a window is never read from concurrently with a write into it.</summary>
/// <remarks><para>Busy time is read off the engine's own bookkeeping
/// (<c>ServerMain.StatsCollector[StatsCollectorIndex].tickTimes</c>), the exact number the
/// engine's "Server overloaded. A tick took {n}ms" warning is computed from, not a stopwatch
/// wrapped around <c>Process()</c> from the outside: the engine's pacing sleep
/// (<c>Thread.Sleep</c>) runs INSIDE <c>Process()</c>, after the busy work and before return, so
/// timing the call from outside would count the sleep as busy time - exactly the number this
/// feature promises to exclude. See docs/specs/2026-09-23-tick-timing.md for the decompiled
/// method and its measured resolution.</para>
/// <para><c>ServerMain.StatsCollector</c>, <c>StatsCollectorIndex</c> and every field of
/// <c>StatsCollection</c> read here are public on every engine version Atlas supports (1.21.7
/// through 1.22.7, verified by decompile), so this reads them directly through the compiled
/// engine reference rather than through <see cref="Bootstrap.EngineCompat"/>: a shape change on
/// a future engine or fork would surface as a runtime <c>MissingFieldException</c> the first
/// time a window is open, not as an <see cref="Bootstrap.EngineCompat"/> degrade path. The field
/// read lives in its own <see cref="ReadBusyTimeMs"/>, marked
/// <see cref="MethodImplOptions.NoInlining"/> and called only once a window is open: keeping it
/// out of <see cref="RecordPass"/>'s own method body means the JIT only has to resolve those
/// fields the first time a measurement actually runs, not on <see cref="RecordPass"/>'s first
/// call from the pump - which happens on every host's very first pass, whether or not anything
/// is being measured.</para></remarks>
internal sealed class PassTimingCollector
{
    private readonly List<List<long>> _activeWindows = [];

    /// <summary>Opens a fresh measurement window, independent of any other window already open.</summary>
    /// <returns>A handle for this window. Pass it back to <see cref="StopAndCollect"/> to close
    /// just this one; every other open window is unaffected.</returns>
    public List<long> Start()
    {
        List<long> window = [];
        _activeWindows.Add(window);
        return window;
    }

    /// <summary>Closes the measurement window identified by <paramref name="window"/> and
    /// returns what it collected.</summary>
    /// <param name="window">The handle <see cref="Start"/> returned for this window.</param>
    /// <returns>The busy-time samples recorded since <see cref="Start"/>, in milliseconds,
    /// oldest first. Empty if no pass was sampled while the window was open.</returns>
    public IReadOnlyList<long> StopAndCollect(List<long> window)
    {
        _activeWindows.Remove(window);
        return window;
    }

    /// <summary>Records the just-completed pass's busy time into every open window. Called by
    /// the pump immediately after every <c>server.Process()</c> call, mirroring
    /// <see cref="EntitySimulationTickCounter.Sample"/>: cheap enough to call unconditionally
    /// rather than branch on whether a window is open, since the common case (no window open) is
    /// just the list-count check below.</summary>
    /// <param name="server">The just-processed server.</param>
    public void RecordPass(ServerMain server)
    {
        if (_activeWindows.Count == 0)
        {
            return;
        }

        RecordSample(ReadBusyTimeMs(server));
    }

    /// <summary>Appends one busy-time sample to every currently open window. Split out from
    /// <see cref="RecordPass"/> so the fan-out itself - the part an overlapping-window bug would
    /// live in - is testable with plain numbers, no live <c>ServerMain</c> needed.</summary>
    /// <param name="ms">The busy time to record, in milliseconds.</param>
    internal void RecordSample(long ms)
    {
        foreach (List<long> window in _activeWindows)
        {
            window.Add(ms);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ReadBusyTimeMs(ServerMain server)
    {
        StatsCollection stats = server.StatsCollector[server.StatsCollectorIndex];
        int lastIndex = PassTimingStatistics.LastWrittenIndex(stats.tickTimes.Length, stats.tickTimeIndex);
        return stats.tickTimes[lastIndex];
    }
}
