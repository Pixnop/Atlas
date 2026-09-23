# 0009. Tick timing reads the engine's own bookkeeping, not a wrapping stopwatch

Status: accepted (`docs/specs/2026-09-23-tick-timing.md`).

## Context

A mod author reported using Atlas "to profile code paths; it's not really built for it, but
once you have a live running thing there's no limit". A third-party harness (Pharos) ships a
benchmark-baseline JSON plus a CI gate as a process pattern worth taking. Both point at the
same gap: Atlas can boot a real server and run a real mod, but has no supported way to say how
expensive a pass through it was.

The obvious approach - wrap a stopwatch around the pump's `server.Process()` call - measures
the wrong thing. Decompiling `ServerMain.Process()` (1.21.7, 1.22.3, 1.22.7, identical) shows
the engine's own pacing sleep runs INSIDE `Process()`, after the busy work and before return:
`elapsedMilliseconds2 = lastFramePassedTime.ElapsedMilliseconds; ... Thread.Sleep(Math.Max(0,
Config.TickTime - elapsedMilliseconds2))`. A pass that does 5ms of real work still takes ~33ms
wall time (the default `TickTime`), because the engine sleeps off the remainder to hold its
pacing. Timing `Process()` from outside would report ~33ms for both a cheap and a moderately
expensive mod tick handler, right up until the mod's cost exceeds the pacing budget - exactly
backwards from what "profile code paths" needs, since a mod that stays under budget is the
common case a mod author wants visibility into, not the pathological one.

## Decision

Read the busy time the engine already computed for its own overload warning
("Server overloaded. A tick took {n}ms") instead of re-timing the call:
`ServerMain.StatsCollector[StatsCollectorIndex].tickTimes[...]`, a rolling 10-slot record kept
per pass, written before the pacing sleep. `ServerMain.StatsCollector`, `StatsCollectorIndex`,
and every field of `StatsCollection` are public on every version Atlas supports (verified by
decompile, 1.21.7 through 1.22.7 - see the spec). That makes this a compiled, compile-checked
reference, not a reflective one: unlike `EntitySimulationTickCounter` (ADR text in
`docs/specs/2026-07-14-tick-contract.md`), this needs no `EngineCompat` shape probe and no
runtime degrade path, because a shape change here is a build break on the next Atlas release,
caught before any test runs at all.

The pump (`ServerHost.Pump`) samples this once per pass into `PassTimingCollector`, which only
retains samples while a measurement window is open (`IWorldSession.MeasureTicks`). Game-thread
allocations (`GC.GetAllocatedBytesForCurrentThread`) are measured once, before and after the
whole windowed wait, not per pass: everything runs on the single game thread by construction
(the pump and the scenario's scheduler share it), so a before/after delta across the awaited
window is exactly the sum of what per-pass sampling would have given, without paying two extra
calls on every single pass of every host's life. `Ticks(n)`/`Until` pacing is unchanged; the
new surface only observes what already runs.

## Consequences

- Resolution is the engine's own: whole milliseconds
  (`Stopwatch.ElapsedMilliseconds`-truncated). A pass under a millisecond reads as 0, which the
  measured spec figures show is the common case for an idle world - not a bug, but the
  documented ceiling of this technique.
- The measurement cannot attribute cost to a specific mod, method or line - it is a
  live-server profiling tool, not an instrumenting profiler, and `IWorldSession.MeasureTicks`'s
  own XML docs say so.
- Nothing here needs a new row in the reflective engine contract theory
  (`tests/Atlas.Pure.Tests/Bootstrap/EngineContractTests.cs`): that net exists for members
  Atlas resolves by reflection, and these are not. The decompile verification across the
  supported range lives in the spec instead.
- A CI baseline recipe (a JSON of expected p95 per scenario, compared with a tolerance) is
  documented as a pattern, not shipped as a hard gate: shared runners are noisy enough that a
  tolerance-free gate would flake on unrelated load, not on a real regression.

## Source files

- `src/Atlas/Internal/Hosting/PassTimingStatistics.cs`: the pure core - `LastWrittenIndex`
  at `:26`, `Compute` at `:43`.
- `src/Atlas/Internal/Hosting/PassTimingCollector.cs`: the shell - `Start` at `:35`,
  `StopAndCollect` at `:41`, `RecordPass` (the engine read) at `:53`.
- `src/Atlas/Internal/Hosting/ServerHost.cs:536`: the collector created alongside the rest of
  `Booted`; `:557`: the pump's per-pass sample.
- `src/Atlas/Internal/Hosting/WorldSession.cs:180`: `MeasureTicks`, the windowed wait plus the
  before/after allocation delta.
- `src/Atlas/Api/IWorldSession.cs:191`, `src/Atlas/Api/TickMeasurement.cs`,
  `src/Atlas/Api/PassTimingStats.cs`: the public surface and its XML docs on what is and is not
  measured.
