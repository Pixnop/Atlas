namespace Atlas.Api;

/// <summary>Per-pass busy-time statistics over a <see cref="TickMeasurement"/> window, in
/// milliseconds, by nearest-rank (every value here was an actually-sampled pass, never an
/// average of two).</summary>
/// <param name="MinMs">The fastest pass in the window.</param>
/// <param name="MedianMs">The middle pass: the typical cost. For an even-sized window, the
/// lower of the two middle values (nearest-rank, never an average of the two).</param>
/// <param name="P95Ms">The 95th-percentile pass: 95% of passes in the window were at or under
/// this.</param>
/// <param name="MaxMs">The slowest pass in the window.</param>
/// <remarks>Resolution is the engine's own: these come from
/// <c>ServerMain.StatsCollector[...].tickTimes</c>, the same rolling record the engine's own
/// "Server overloaded" warning reads, truncated to whole milliseconds
/// (<see cref="System.Diagnostics.Stopwatch.ElapsedMilliseconds"/>). A pass busy for under a
/// millisecond reads as 0 - common on an idle world with a light mod, and not a measurement bug.
/// See docs/specs/2026-09-23-tick-timing.md for the measured noise floor on this machine.</remarks>
public sealed record PassTimingStats(long MinMs, long MedianMs, long P95Ms, long MaxMs)
{
    /// <summary>The mean of the per-pass busy times in the window, in milliseconds: the sum of the
    /// samples (<see cref="TotalMs"/>) divided by the number of passes sampled
    /// (<see cref="TickMeasurement.Passes"/>).</summary>
    /// <remarks><para>This is the mean of the numbers the engine itself measures per pass, and each
    /// of those is already a whole number of milliseconds (see the remarks on
    /// <see cref="PassTimingStats"/>). The result can be fractional, but it is not a
    /// sub-millisecond timing: every pass is floored before it is counted, so the mean is never
    /// above the true mean and is less than one millisecond below it. A world whose passes each
    /// really cost 1.4 ms reads 1.0.</para>
    /// <para>The engine keeps a running pair for the same samples, <c>tickTimeTotal</c> and
    /// <c>ticksTotal</c>, and adds each pass's busy time to both. This mean is that total over that
    /// count, taken over exactly the passes of the window. The engine's own pair restarts from
    /// zero every two seconds, so reading it directly gives the mean of whatever passes fell in
    /// the current two-second bucket, not of a window you chose.</para></remarks>
    public double MeanMs { get; init; }

    /// <summary>The sum of the per-pass busy times in the window, in whole milliseconds. Each pass
    /// is counted as the engine measured it (floored to a millisecond, see the remarks on
    /// <see cref="PassTimingStats"/>), so this is less than one millisecond per pass below the
    /// true total.</summary>
    public long TotalMs { get; init; }
}
