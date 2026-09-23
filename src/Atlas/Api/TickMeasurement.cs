namespace Atlas.Api;

/// <summary>The outcome of <see cref="IWorldSession.MeasureTicks"/>: what the game thread did
/// across the measured window, and what it cost.</summary>
/// <param name="Passes">The number of engine passes (<c>ServerMain.Process()</c> calls)
/// actually sampled while the wait was pending. Normally equal to the tick count requested - a
/// pass fires the tick listener <see cref="IWorldSession.Ticks"/> waits on once per pass under
/// the engine's default pacing, see docs/specs/2026-07-14-tick-contract.md - but this is the
/// measured count, not the requested one: an engine that paces differently, or a pass that ran
/// without firing the listener, moves this number and not the argument passed to
/// <see cref="IWorldSession.MeasureTicks"/>.</param>
/// <param name="BusyTime">Per-pass busy-time statistics across the window (see
/// <see cref="PassTimingStats"/> for what "busy" excludes and its resolution).</param>
/// <param name="WallTime">Total wall time the wait actually took, pacing sleep included: what a
/// caller watching the clock would have measured, as opposed to <see cref="BusyTime"/>.</param>
/// <param name="AllocatedBytes">Bytes allocated on the game thread across the window, from
/// <see cref="System.GC.GetAllocatedBytesForCurrentThread"/> sampled before and after the wait.
/// Game-thread only: allocations on the engine's other threads (networking, chunk generation,
/// the background assets build) are not included, and this is a delta of a process-wide
/// generational counter, so a concurrent full GC on another thread can occasionally perturb it
/// by a small, one-off amount - see docs/specs/2026-09-23-tick-timing.md for the measured
/// noise.</param>
public sealed record TickMeasurement(int Passes, PassTimingStats BusyTime, TimeSpan WallTime, long AllocatedBytes);
