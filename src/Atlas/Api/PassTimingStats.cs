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
public sealed record PassTimingStats(long MinMs, long MedianMs, long P95Ms, long MaxMs);
