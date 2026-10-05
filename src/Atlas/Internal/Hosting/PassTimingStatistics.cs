using Atlas.Api;

namespace Atlas.Internal.Hosting;

/// <summary>Pure core behind <see cref="Api.IWorldSession.MeasureTicks"/>: which slot in a
/// rotating fixed-size buffer a write cursor just wrote (<see cref="LastWrittenIndex"/>), and
/// turning a window of per-pass busy-time samples into <see cref="PassTimingStats"/>
/// (<see cref="Compute(IReadOnlyList{long})"/>), both testable with plain numbers, no engine
/// types. The live read of the engine's own per-pass bookkeeping stays in the thin shell,
/// <see cref="PassTimingCollector"/>.</summary>
/// <remarks>The buffer shape mirrors <c>Vintagestory.Server.StatsCollection</c> exactly (a
/// fixed-length <c>long[] tickTimes</c> plus a <c>tickTimeIndex</c> cursor the engine leaves
/// pointing at the NEXT slot to write, after writing the current pass's time into the slot
/// before it - see docs/specs/2026-09-23-tick-timing.md for the decompiled write order, verified
/// identical on 1.21.7, 1.22.3 and 1.22.7).</remarks>
internal static class PassTimingStatistics
{
    /// <summary>Which slot of a rotating fixed-size buffer of length <paramref name="length"/>
    /// holds the most recently written value, given the write cursor's CURRENT position (the
    /// slot the NEXT write will land on).</summary>
    /// <param name="length">The buffer's length. Must be at least 1.</param>
    /// <param name="cursor">The write cursor's current position; not required to already be in
    /// range (the engine's own cursor always is, but the modulo below makes this total anyway).</param>
    /// <returns>The index of the most recently written slot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is
    /// less than 1.</exception>
    public static int LastWrittenIndex(int length, int cursor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        // Plain "% length" can return a negative result for a negative cursor; the extra
        // "+ length, % length" folds that back into [0, length).
        return (((cursor - 1) % length) + length) % length;
    }

    /// <summary>Computes min/median/p95/max over a window of per-pass busy-time samples, by
    /// nearest-rank (no interpolation): every one of those is a value that was actually sampled,
    /// never an average of two. The mean and the total are the exception, by definition: they
    /// are the plain sum of the samples and that sum over their count.</summary>
    /// <param name="samplesMs">The busy-time samples, in milliseconds, one per pass. Order does
    /// not matter; the values themselves are ranked.</param>
    /// <returns>The computed statistics.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="samplesMs"/> is empty:
    /// there is no window without at least one pass to have sampled.</exception>
    public static PassTimingStats Compute(IReadOnlyList<long> samplesMs)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);
        if (samplesMs.Count == 0)
        {
            throw new ArgumentException(
                "Cannot compute pass-timing statistics over an empty window (zero passes sampled).",
                nameof(samplesMs));
        }

        long[] sorted = [.. samplesMs];
        Array.Sort(sorted);
        long total = sorted.Sum();
        return new PassTimingStats(
            MinMs: sorted[0],
            MedianMs: Percentile(sorted, 0.50),
            P95Ms: Percentile(sorted, 0.95),
            MaxMs: sorted[^1])
        {
            MeanMs = (double)total / sorted.Length,
            TotalMs = total,
        };
    }

    /// <summary>Computes the statistics of <see cref="Compute(IReadOnlyList{long})"/> and adds the
    /// mean of the stopwatch figures, <see cref="PassTimingStats.MeanMicroseconds"/>.</summary>
    /// <param name="samplesMs">The engine's busy-time samples, in milliseconds, one per pass.</param>
    /// <param name="samplesMicroseconds">Atlas's own figure for the same passes, in microseconds
    /// (see <see cref="BusyMicroseconds"/>), in the same order.</param>
    /// <returns>The computed statistics.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="samplesMs"/> is empty, or
    /// when the two windows are not the same length.</exception>
    public static PassTimingStats Compute(IReadOnlyList<long> samplesMs, IReadOnlyList<double> samplesMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);
        ArgumentNullException.ThrowIfNull(samplesMicroseconds);
        if (samplesMicroseconds.Count != samplesMs.Count)
        {
            throw new ArgumentException(
                $"The microsecond window has {samplesMicroseconds.Count} samples and the millisecond window has {samplesMs.Count}: they describe the same passes.",
                nameof(samplesMicroseconds));
        }

        return Compute(samplesMs) with { MeanMicroseconds = samplesMicroseconds.Sum() / samplesMicroseconds.Count };
    }

    /// <summary>One pass's busy time in microseconds, from the wall time of the whole
    /// <c>Process()</c> call: that time less the pacing sleep the engine asked for, held inside the
    /// whole millisecond the engine measured for the pass.</summary>
    /// <param name="wallMicroseconds">How long <c>Process()</c> took from outside, sleep included.</param>
    /// <param name="engineBusyMs">The engine's own sample of the pass, whole milliseconds, taken
    /// just before it sleeps.</param>
    /// <param name="tickTimeMs">The engine's pacing budget, <c>ServerConfig.TickTime</c>.</param>
    /// <returns>The pass's busy time in microseconds.</returns>
    /// <remarks>The engine sleeps <c>(int)Math.Max(0f, TickTime - (float)busyMs)</c> milliseconds
    /// (decompiled on 1.21.7, 1.22.3 and 1.22.7, identical), and its sample is the pass's busy time
    /// floored to a millisecond, so the true busy time is at least the sample and below the sample
    /// plus one millisecond. What the sleep adds on top of what the engine asked for, the operating
    /// system's overshoot, stays in the result, which the clamp keeps from ever leaving that
    /// millisecond.</remarks>
    public static double BusyMicroseconds(double wallMicroseconds, long engineBusyMs, float tickTimeMs)
    {
        int requestedSleepMs = (int)Math.Max(0f, tickTimeMs - (float)engineBusyMs);
        double estimate = wallMicroseconds - (requestedSleepMs * 1000.0);
        return Math.Clamp(estimate, engineBusyMs * 1000.0, (engineBusyMs + 1) * 1000.0);
    }

    /// <summary>Nearest-rank percentile: the smallest value at or above which
    /// <paramref name="p"/> of the sorted samples fall.</summary>
    /// <param name="sorted">The samples, already sorted ascending.</param>
    /// <param name="p">The percentile, in [0, 1].</param>
    /// <returns>The sample at that rank.</returns>
    private static long Percentile(long[] sorted, double p)
    {
        int rank = (int)Math.Ceiling(p * sorted.Length);
        int index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
