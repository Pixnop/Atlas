using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Collects one server pass's busy time - <c>Process()</c> minus the engine's own
/// pacing sleep - for every pass sampled while a <see cref="Api.IWorldSession.MeasureTicks"/>
/// window is open. Single-thread-confined to the game thread, like
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
/// engine reference rather than through <see cref="Bootstrap.EngineCompat"/>: a shape change
/// here is a build break on the next Atlas release, not a runtime drift to probe for and degrade
/// - stronger, not weaker, than the reflection-based signals this file sits next to
/// (<see cref="EntitySimulationTickCounter"/>).</para></remarks>
internal sealed class PassTimingCollector
{
    private List<long>? _active;

    /// <summary>Opens a fresh measurement window. Discards anything an earlier window left
    /// uncollected: the only way that happens is the earlier window's tick wait throwing (a host
    /// crash mid-measurement), never a normal completion, since every caller that opens a window
    /// also closes it.</summary>
    public void Start() => _active = [];

    /// <summary>Closes the measurement window and returns what it collected.</summary>
    /// <returns>The busy-time samples recorded since <see cref="Start"/>, in milliseconds,
    /// oldest first. Empty if no pass was sampled while the window was open, or if no window was
    /// ever opened.</returns>
    public IReadOnlyList<long> StopAndCollect()
    {
        List<long> samples = _active ?? [];
        _active = null;
        return samples;
    }

    /// <summary>Records the just-completed pass's busy time, if a window is open. Called by the
    /// pump immediately after every <c>server.Process()</c> call, mirroring
    /// <see cref="EntitySimulationTickCounter.Sample"/>: two public field reads and a list add at
    /// most, cheap enough to call unconditionally rather than branch on whether a window is open.</summary>
    /// <param name="server">The just-processed server.</param>
    public void RecordPass(ServerMain server)
    {
        if (_active is not { } active)
        {
            return;
        }

        StatsCollection stats = server.StatsCollector[server.StatsCollectorIndex];
        int lastIndex = PassTimingStatistics.LastWrittenIndex(stats.tickTimes.Length, stats.tickTimeIndex);
        active.Add(stats.tickTimes[lastIndex]);
    }
}
