# Tick timing (wiki sections, not a standalone page)

The other files in this folder are retired redirects: the wiki itself lives on GitHub, not in
this repo (see `getting-started.md`/`architecture.md`/`writing-scenarios.md`). This file is not
a redirect either: it is two sections ready to paste into the wiki, kept here because those
pages' content is not (see `boot-diagnostics.md` for the pattern this follows).

---

## Measuring the server-side cost of a scenario

(For the **Writing Scenarios** page, next to whatever it already says about `Ticks`/`Until`.)

`World.MeasureTicks(count)` runs `count` ticks, the same wait `Ticks(n)` uses, while watching
what the game thread did across them:

```csharp
await World.Ticks(20); // let the boot's tail end settle first, same as any tick-sensitive assertion

TickMeasurement measured = await World.MeasureTicks(100);

Assert.True(measured.BusyTime.MedianMs < 5, $"median pass grew to {measured.BusyTime.MedianMs}ms");
```

`TickMeasurement` reports:

- `Passes`: engine passes actually sampled (measured, not assumed equal to the requested count).
- `BusyTime`: min/median/p95/max per-pass busy time in milliseconds, excluding the engine's own
  pacing sleep - the number the engine's own "Server overloaded" warning is computed from, not a
  stopwatch wrapped around the pass from outside.
- `WallTime`: the whole wait's wall-clock time, pacing sleep included.
- `AllocatedBytes`: game-thread allocations across the window (`GC.GetAllocatedBytesForCurrentThread`,
  an exact per-thread count, not a process-wide one).

It is a profiling tool built from a live running server, not an instrumenting profiler: it
cannot attribute cost to a specific mod, method or line, only to "this window of N ticks", and
it cannot see work the engine does off the game thread (networking, chunk generation, the
background assets build). Busy time is read at whole-millisecond resolution, so a fast, idle
pass commonly reads as 0ms - not a bug, the documented ceiling of the technique. Prefer
comparing medians or p95s across repeated windows over trusting one window's numbers alone.

## CI performance baselines

(For a **CI Recipes** page.)

`MeasureTicks` can back a CI performance-baseline gate, the way a benchmark-baseline JSON plus a
CI job does in other harnesses: keep a small `tick-timing-baselines.json` (one entry per watched
scenario, an expected p95 busy-time-per-pass in milliseconds plus a tolerance), have a scenario
call `MeasureTicks` over a representative window and write its `BusyTime.P95Ms` out, and compare
the two with a short script that fails only when the measured p95 exceeds `expectedP95Ms +
toleranceMs`:

```json
{ "version": 1, "baselines": { "MyExpensiveTickHandler": { "expectedP95Ms": 8, "toleranceMs": 4 } } }
```

```python
import json, sys

baselines = json.load(open("tick-timing-baselines.json"))["baselines"]
result = json.load(open("tick-timing-results.json"))
baseline = baselines[result["scenario"]]
limit = baseline["expectedP95Ms"] + baseline["toleranceMs"]

if result["p95Ms"] > limit:
    sys.exit(f"{result['scenario']}: p95 {result['p95Ms']}ms exceeds {limit}ms")
```

Keep the tolerance: wall time and allocations both carry real run-to-run spread even on a single
quiet machine, and a shared CI runner is noisier still. A gate without a tolerance fails on
scheduling noise as often as on a real regression. See
`docs/specs/2026-09-23-tick-timing.md` for the full recipe and the measured noise it is based on.
