using System.Globalization;
using System.Text;

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
/// <see cref="MeanMicroseconds"/> is the one figure that is not the engine's: a stopwatch's, for
/// the passes that floor to 0. See docs/specs/2026-09-23-tick-timing.md for the measured noise floor
/// on this machine.</remarks>
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

    /// <summary>The mean of the per-pass busy times in the window, in microseconds, from a
    /// stopwatch Atlas runs around each <c>ServerMain.Process()</c> call, with the engine's
    /// pacing sleep taken out. The figure to read when passes cost less than a few
    /// milliseconds, where <see cref="MeanMs"/> cannot resolve them.</summary>
    /// <remarks><para><b>What the stopwatch covers.</b> It starts before <c>Process()</c> and stops
    /// after it returns, so it covers the same pass the engine's samples do: the engine's server
    /// systems, the game-tick event (a mod's own tick listeners), and the engine's handling of the
    /// packets and main-thread tasks it queued. It does not cover what Atlas does between two
    /// passes, where the scenario's own continuations run. The engine ends every pass by sleeping off
    /// what is left of its tick budget (<c>ServerConfig.TickTime</c>, about 33.3 ms) inside
    /// <c>Process()</c>, so the stopwatch alone reads about 33 ms for a pass that did almost
    /// nothing (33.1 ms for an idle world, measured on 1.22.3). Atlas takes the sleep out: it
    /// subtracts the whole milliseconds the engine asked to sleep, worked out from the engine's own
    /// sample of the pass the way the engine works them out, and holds each pass's result inside the
    /// whole millisecond that sample gives (never below it, never above it plus one millisecond).</para>
    /// <para><b>How it differs from the engine's samples.</b> The engine's busy time is a
    /// <c>Stopwatch</c> read in whole milliseconds just before the sleep (see
    /// <see cref="MeanMs"/>). This one is not floored, so it resolves passes the engine reads as 0:
    /// on 1.21.7, 1.22.3 and 1.22.7 an idle world read 100 to 135 microseconds a pass where
    /// <see cref="MeanMs"/> read 0, and a deliberate 300 microsecond spin in a tick listener read 418
    /// to 432. It is also a little high. It keeps what the operating system adds to the engine's
    /// <c>Thread.Sleep</c>, a median of 52 to 62 microseconds on Linux 6.18 when measured on its
    /// own, and the engine's bookkeeping between its reading and the sleep, so the spin reads about
    /// 120 above its length, which is what an idle pass reads. Compare two figures from the same
    /// machine, a mod against vanilla or before against after, rather than reading one as the exact
    /// cost of a pass. Where the operating system rounds sleeps up further, as Windows can at its
    /// default timer resolution, the overshoot can exceed the work, and the figure then sits at the
    /// top of the engine's millisecond. That case was not measured.</para>
    /// <para>The existing figures are unchanged: <see cref="MinMs"/>, <see cref="MedianMs"/>,
    /// <see cref="P95Ms"/>, <see cref="MaxMs"/>, <see cref="MeanMs"/> and <see cref="TotalMs"/> stay
    /// the engine's samples, so they stay comparable with what they have always been.</para></remarks>
    public double MeanMicroseconds { get; init; }

    // The synthesized ToString would format MeanMs with the current culture ("0,005" under fr-FR);
    // this prints the same text with an invariant one, so a pasted log reads the same everywhere.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(
            CultureInfo.InvariantCulture,
            $"MinMs = {MinMs}, MedianMs = {MedianMs}, P95Ms = {P95Ms}, MaxMs = {MaxMs}, MeanMs = {MeanMs}, TotalMs = {TotalMs}, MeanMicroseconds = {MeanMicroseconds}");
        return true;
    }
}
