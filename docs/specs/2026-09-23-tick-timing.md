# Tick timing: measuring the server-side cost of a mod

Date: 2026-09-23
Status: measured and implemented: `IWorldSession.MeasureTicks` shipped; `Ticks(n)`/`Until`
pacing deliberately unchanged
Tracks: a mod author's field report ("I used Atlas to profile code paths; it's not really
built for it, but once you have a live running thing there's no limit") and a review of the
Pharos harness (MIT), whose benchmark-baseline-JSON-plus-CI-gate pattern is worth taking as a
process, not as code
Game versions measured: 1.22.3 (the default install, run live), 1.21.7 and 1.22.7 (decompiled,
targeted, to bracket the CI matrix's floor and latest)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md), [the tick
contract](2026-07-14-tick-contract.md)

## Motivation

Atlas boots a real, live embedded server; a mod author using it for functional tests
eventually wants to know whether a change made the mod slower, and Atlas had nothing to offer
beyond a scenario's own wall-clock, which is dominated by the engine's ~33ms pacing sleep and
tells you nothing about a mod's actual CPU cost. This pass makes that a supported, honestly
documented feature: a scoped measurement over a run of ticks, reporting per-pass busy time,
pass count, wall time and game-thread allocations, with its resolution and blind spots written
down rather than implied.

## Method

- Decompilation (ilspycmd 11.0.0) of `ServerMain.Process()` and `StatsCollection` against the
  default install (1.22.3) and, to bracket the CI matrix, the 1.21.7 floor and 1.22.7 latest
  installs already cached on this machine for compat testing.
- Live instrumented runs against the built `IWorldSession.MeasureTicks`: a vanilla host and a
  host with a fixture mod (`TickTimingFixtureMod`, a tick listener that busy-spins for a known
  20ms every tick), 5 repeated boots each, 100-tick measurement windows, values printed and
  compared by hand (the temporary probe was a throwaway test, deleted after measurement - the
  same discipline the tick-contract spec used).

## How the engine paces a pass, and where the busy time actually lives

`ServerMain.Process()`, identical in structure on 1.21.7, 1.22.3 and 1.22.7:

```csharp
lastFramePassedTime.Restart();
// ... tick server systems, EventManager.TriggerGameTick (mod tick listeners run here),
// ... ProcessMain() ...
long elapsedMilliseconds2 = lastFramePassedTime.ElapsedMilliseconds;   // the busy time
StatsCollection statsCollection = StatsCollector[StatsCollectorIndex];
statsCollection.tickTimeTotal += elapsedMilliseconds2;
statsCollection.ticksTotal++;
statsCollection.tickTimes[statsCollection.tickTimeIndex] = elapsedMilliseconds2;
statsCollection.tickTimeIndex = (statsCollection.tickTimeIndex + 1) % statsCollection.tickTimes.Length;
int num3 = (int)Math.Max(0f, Config.TickTime - (float)elapsedMilliseconds2);
if (num3 > 0) { Thread.Sleep(num3); }                                   // the pacing sleep
```

The pacing sleep runs INSIDE `Process()`, after the busy work is already accounted for. A
stopwatch wrapped around the pump's call to `Process()` from the outside would time busy work
PLUS the sleep, which for any pass under the ~33ms budget (`Config.TickTime`, default
`33.333332f`) rounds to "about 33ms" regardless of whether the busy work was 1ms or 20ms -
useless for exactly the case a mod author cares about (a handler that stays inside budget).
Confirmed live: see the wall-time row in the measured table below, identical between the
vanilla and 20ms-spin fixture runs.

The engine already computes and keeps the number that matters - `elapsedMilliseconds2`, the
busy time excluding the sleep - in `ServerMain.StatsCollector[StatsCollectorIndex].tickTimes`,
a rolling 10-slot-per-collector record, 4 collectors rotated every 2 seconds (the same numbers
the "Server overloaded. A tick took {n}ms" warning reads). Both `StatsCollector` and
`StatsCollectorIndex` on `ServerMain`, and every field of `StatsCollection`
(`tickTimes`, `tickTimeIndex`, `tickTimeTotal`, `ticksTotal`), are **public** on every version
checked:

| Member | 1.21.7 | 1.22.3 | 1.22.7 |
|---|---|---|---|
| `ServerMain.StatsCollector` (`StatsCollection[]`) | public | public | public |
| `ServerMain.StatsCollectorIndex` (`int`) | public | public | public |
| `StatsCollection.tickTimes` (`long[10]`) | public | public | public |
| `StatsCollection.tickTimeIndex` (`int`) | public | public | public |

Because these are public, ordinary compiled fields, Atlas reads them directly
(`PassTimingCollector.ReadBusyTimeMs`) rather than through `EngineCompat`'s reflective shape
probing (ADR 0003): for every engine version Atlas builds against, the compiler already catches
a shape change here, so there is nothing left for a runtime probe to add. That coverage stops
at the versions Atlas builds against, though - it does not reach a released Atlas binary run
against a newer engine or a fork, ADR 0003's other case (the "Prebuilt cross-install" CI lane).
Drift there would surface as a runtime `MissingFieldException`, not a build break; the read is
isolated to a method called only while a `MeasureTicks` window is open, so that failure would
be scoped to `MeasureTicks` callers, not every host's pump. This is why the pass-timing signal
has no row in the reflective engine contract theory
(`tests/Atlas.Pure.Tests/Bootstrap/EngineContractTests.cs`, ADR 0009): that net exists for
members resolved by reflection, and this one is not - the compiler is the earlier, harder check
within the supported build range, and the scoped read handles what falls outside it.

The write order matters for reading it back correctly: the engine writes
`tickTimes[tickTimeIndex]` and THEN advances `tickTimeIndex`, so immediately after `Process()`
returns, the just-written value sits one slot BEHIND the current cursor
(`PassTimingStatistics.LastWrittenIndex`, pure-tested against the wrap-around case).

## The API

`IWorldSession.MeasureTicks(int count)` runs `count` ticks (same `Ticks(n)` wait underneath,
pacing unchanged) while `PassTimingCollector` samples every pass's busy time off
`StatsCollector`, and returns a `TickMeasurement`:

```csharp
public sealed record TickMeasurement(int Passes, PassTimingStats BusyTime, TimeSpan WallTime, long AllocatedBytes);
public sealed record PassTimingStats(long MinMs, long MedianMs, long P95Ms, long MaxMs);
```

- `Passes`: the number of `Process()` calls actually sampled - measured, not assumed equal to
  `count` (see the tick contract spec for why passes and ticks are not a guaranteed 1:1, even
  though they measure 1:1 on every supported vanilla engine under default pacing).
- `BusyTime`: min/median/p95/max over the window, by nearest-rank (every value was an actually
  sampled pass; no interpolated average).
- `WallTime`: the whole wait's wall time, pacing sleep included - what a caller watching the
  clock experiences, contrasted against `BusyTime`.
- `AllocatedBytes`: `GC.GetAllocatedBytesForCurrentThread()` sampled once before and once after
  the wait, on the game thread. This is safe to take once rather than once per pass because
  everything runs on the single game thread by construction (the pump and the awaited
  continuation share it - see ADR 0001), so a before/after delta over the whole window equals
  the sum a per-pass sample would have given, without the extra cost of two calls per pass on
  every host's entire life regardless of whether anything is measuring.

## Measured: noise on this machine

5 repeated boots each, 100-tick windows, after a 20-tick settle:

| Scenario | Passes | BusyTime min/median/p95/max (ms) | Wall time (ms) | Allocated (bytes, game thread) |
|---|---|---|---|---|
| Vanilla (no mod) | 100, every run | 0 / 0 / 0 / 0, every run | 3307.0 - 3314.2 | 133,160 - 145,640 |
| `TickTimingFixtureMod` (20ms spin/tick) | 100, every run | 20 / 20 / 20 / 20, every run | 3307.0 - 3308.0 | 132,776 - 134,960 |

Findings:

- **Pass count tracked the requested tick count exactly, every run, both scenarios** (100
  requested, 100 sampled) - the 1:1 pacing relationship the tick-contract spec measured for
  entity-simulation ticks holds for pump passes too, on this machine, under default pacing.
  `TickMeasurement.Passes` is still reported as measured rather than hard-coded to the request,
  because nothing in the engine promises the ratio (same caveat as `Ticks(n)`).
- **Busy time has essentially zero measurement noise, in both directions.** An idle vanilla
  world reads exactly 0ms on every one of 500 sampled passes (100 x 5 runs): real per-pass work
  on an idle world is genuinely sub-millisecond, and the engine's ms-truncated resolution
  cannot see it - a real ceiling of this technique, not a bug, and worth knowing before reading
  a single "0ms" measurement as "did nothing". A deliberately expensive callback (20ms) reads
  exactly 20ms on all 500 sampled passes: no jitter was observed at all at this resolution and
  this load level. Sub-millisecond and few-millisecond mod costs will be far noisier in
  practice (a callback costing 1-3ms will likely alternate between reading 0 and a few ms
  depending on where the millisecond truncation falls); this was not separately measured and is
  a documented gap, not a claim.
- **Wall time is governed by the pacing budget, not the busy time, for any pass under
  budget** - exactly the reasoning that ruled out an outside stopwatch (see above). The 20ms
  spin and the idle world produced statistically indistinguishable wall times (~3307ms for 100
  ticks, ~33.07ms/tick, matching `Config.TickTime`'s default `33.333332f` closely); only a
  busy time exceeding the ~33ms budget would show up in wall time at all, which is a much
  cruder and later signal than reading `BusyTime` directly.
- **Allocation deltas carry real noise**, roughly 133KB-146KB over a 100-tick vanilla window
  (about 10% run-to-run spread) - this is Atlas's own game-thread bookkeeping plus whatever the
  engine allocates in its own per-pass work, not attributable to a specific mod. The 20ms-spin
  fixture's own allocations (one `Stopwatch` object per tick - a few kilobytes total across a
  100-tick window, not the sub-kilobyte figure an earlier pass of this section claimed) sat
  close enough to this machine's vanilla spread that the 100-tick table above could not reliably
  tell them apart from noise; a spot check at a smaller window size did see the fixture read
  visibly higher than vanilla. Treat `AllocatedBytes` as a coarse, comparative signal ("did this
  change roughly double allocations over N ticks") rather than an exact count, and prefer
  comparing repeated windows over trusting one.

## A CI baseline recipe (pattern, not a shipped gate)

Reviewing Pharos (MIT, a third-party harness) for what Atlas should take from it: its
benchmark-baseline JSON plus a CI job that validates it is a useful process pattern (thanks are
due for surfacing it; nothing here is Pharos's code, and none was copied). The shape:

1. A small JSON file, e.g. `tick-timing-baselines.json`, one entry per scenario the project
   wants watched, each an expected p95 busy-time-per-pass in milliseconds plus a tolerance:

   ```json
   {
     "version": 1,
     "baselines": {
       "MyExpensiveTickHandler": { "expectedP95Ms": 8, "toleranceMs": 4 }
     }
   }
   ```

2. A scenario that calls `MeasureTicks` over a representative window and writes its
   `BusyTime.P95Ms` to a small result file, keyed the same way as the baseline JSON:

   ```csharp
   TickMeasurement measured = await World.MeasureTicks(200);
   File.WriteAllText(
       "tick-timing-results.json",
       $$"""{"scenario": "MyExpensiveTickHandler", "p95Ms": {{measured.BusyTime.P95Ms}}}""");
   ```

3. A short comparison script (not a new tool - about ten lines of Python), run as its own CI
   step after the scenario:

   ```python
   import json, sys

   baselines = json.load(open("tick-timing-baselines.json"))["baselines"]
   result = json.load(open("tick-timing-results.json"))
   baseline = baselines[result["scenario"]]
   limit = baseline["expectedP95Ms"] + baseline["toleranceMs"]

   if result["p95Ms"] > limit:
       sys.exit(
           f"{result['scenario']}: p95 {result['p95Ms']}ms exceeds "
           f"{baseline['expectedP95Ms']}ms + {baseline['toleranceMs']}ms tolerance ({limit}ms)")
   print(f"{result['scenario']}: p95 {result['p95Ms']}ms within {limit}ms")
   ```

Deliberately **not** proposed as a hard, tolerance-free gate on shared CI runners: this pass's
own measurements show wall time and allocations both carry real run-to-run spread on a single
machine, and a shared runner under unrelated load is noisier still. A gate without a tolerance
would fail on scheduling noise as often as on a real regression, training the team to ignore
it. A tolerance wide enough to absorb that noise, reviewed and bumped deliberately when a
change is expected to move the number, is what makes the gate worth keeping.

This recipe is not wired into `.github/workflows/ci.yml` by this pass: it needs a project to
have scenarios worth baselining first (a template, not a requirement), and the tolerance is a
per-project judgment call. The wiki-ready version of this section, plus the `MeasureTicks` usage
section, is in `docs/wiki/tick-timing.md`, for whoever maintains the GitHub wiki to fold in.

## What was skipped, and why

- **A delegate overload of `MeasureTicks`** (run an arbitrary synchronous callback instead of
  waiting on ticks). A mod's own tick listeners already run inside `Process()`, so the
  tick-count form already covers the motivating case (profiling a mod's live tick handler); a
  delegate form would mainly serve timing a single command or synchronous call, which is not
  what the field report or the fixture test needed. Add it if a caller needs to time something
  that is not itself tick-driven.
- **Finer-than-millisecond busy-time resolution** (e.g. reading a private high-resolution
  stopwatch by reflection instead of the engine's own ms-truncated record). Would need a new
  reflective shape probe for a private field, reintroducing exactly the version-drift risk this
  pass avoided by using the engine's own public bookkeeping instead; the measured noise section
  above documents the resulting ceiling honestly instead. Revisit if a mod author needs
  sub-millisecond precision, which nothing so far has asked for. (0.17 did, and took another
  route, a stopwatch of Atlas's own and no private field: see the last addendum.)
- **A shipped, wired-in CI gate.** Documented as a recipe above; not turned on for this repo's
  own CI, which has no scenario yet worth baselining and no owner assigned to review tolerance
  drift.

## Addendum (0.16): the mean and the total

`PassTimingStats` gained `MeanMs` (a `double`) and `TotalMs` (a `long`), init properties computed
in `PassTimingStatistics.Compute` from the samples the window already keeps, so the constructor
and `Deconstruct` did not change. A consumer that trended a mean per-pass busy time had been
reading the engine's `tickTimeTotal` and `ticksTotal` by reflection; this replaces that read.

What the engine does, read in `ServerMain.Process()` on 1.21.7, 1.22.3 and 1.22.7 (the counter
writes are the same on all three): it reads `lastFramePassedTime.ElapsedMilliseconds` once, after the server systems,
the game-tick event and `ProcessMain()` and before the pacing sleep, and that one value goes to
three places: `tickTimeTotal += busy`, `ticksTotal++` and `tickTimes[tickTimeIndex] = busy`.
The samples `PassTimingCollector` reads and the engine's own running pair are therefore the same
numbers, so over a window `TotalMs` is the growth of `tickTimeTotal` and the pass count is the
growth of `ticksTotal`, and `MeanMs` is their ratio.

Two differences from reading the engine's pair directly:

- Every two seconds the pass first moves to the next of four `StatsCollection`s and zeroes it
  (`tickTimeTotal`, `ticksTotal`, `tickTimes`), then adds its own sample to it. Each pass lands
  in exactly one bucket and a bucket holds at most two seconds of passes, so the engine's own
  ratio is the mean of the current bucket. `MeanMs` is the mean of the window the caller chose.
- On 1.22.x, not on 1.21.7, the pass that rolls the bucket over also runs
  `processConnectionQueue()` before the elapsed read, so its sample can read higher than its
  neighbours'. It is the engine's number, counted by both.

A pass that returns early (`Suspended`, or the `Standby` run phase) or throws inside `Process()`
writes no sample, so the collector reads the previous slot again while `ticksTotal` does not grow.
That already held for `Passes` and the median; it applies to the mean and the total as well.

Each sample is a `Stopwatch.ElapsedMilliseconds`, a truncation, so the mean is never above the
true mean and is less than one millisecond below it. That is the same ceiling the median and the
p95 have; the finer mode stays in the skipped list above.

`PassTimingStatisticsTests` pins the arithmetic. `TickTimingTests` pins the relation against a
live server: it reads the current bucket's counters before and after a 10-pass window and
compares them with `TotalMs`, `Passes` and `MeanMs`, retrying a window that spans a rollover.
Checked on 1.22.3 and 1.21.7.

## Addendum (0.17): a microsecond mean

`PassTimingStats` gained `MeanMicroseconds` (a `double`, an init property like `MeanMs` and
`TotalMs`, so the constructor and `Deconstruct` did not change). It exists because a comparison of a
patched server against vanilla reads 0.6 to 1.9 ms a pass, and with the engine's floored samples
roughly half of such a number can be flooring (see the addendum above).

**A stopwatch around the pass reads the sleep.** The obvious figure, a `Stopwatch` around the
pump's `Process()` call, is what the method section rules out as the headline. Measured through the
pump on a world with nothing running, that stopwatch averaged 33.1 ms a pass on all three versions
(33.12, 33.16 and 33.16 ms over 150 passes) while the engine's own sample read 0 ms, because the
pacing sleep is inside the call. So the figure is not the stopwatch time; it is that time less the
sleep the engine asked for.

**What is subtracted.** The engine sleeps `(int)Math.Max(0f, Config.TickTime - (float)busyMs)`
whole milliseconds, with `busyMs` its own sample of the pass (`Process()`, identical on 1.21.7,
1.22.3 and 1.22.7). That sample is the busy time floored to a millisecond, so the true busy time is
at least `busyMs` and under `busyMs + 1` ms. `PassTimingStatistics.BusyMicroseconds` takes the
stopwatch time, removes the requested sleep and clamps the result into that millisecond. The
clamp is what makes the figure safe to publish next to the engine's: it can never contradict the
engine's sample, and where the operating system's sleep overshoot is large it degrades to the
engine's millisecond plus one instead of to garbage. The result still holds the overshoot of
`Thread.Sleep` itself, which cannot be taken out from outside.

**Measured.** Two probes on a machine carrying other test runs (load average 7 to 9), 1.21.7, 1.22.3
and 1.22.7 in the same session. First, `Thread.Sleep(n)` for n of 1, 5, 20 and 33 ms, 100 calls each,
off the game thread: the overshoot had a median of 52 to 62 microseconds, a p95 of 55 to 70 and a
maximum of 76, on all three runs. Second, the shipped `MeasureTicks(150)` over a tick listener that
spins for a fixed time every pass (300 warm-up ticks, then ten more before each window; the first
window of each run is the world with nothing spinning):

| Spin per pass | `MeanMs` 1.22.3 / 1.21.7 / 1.22.7 | `MeanMicroseconds` 1.22.3 / 1.21.7 / 1.22.7 |
|---|---|---|
| none | 0.000 / 0.000 / 0.000 | 102 / 135 / 130 |
| 300 microseconds | 0.000 / 0.013 / 0.000 | 419 / 432 / 418 |
| 600 microseconds | 0.040 / 0.007 / 0.000 | 808 / 715 / 716 |
| 1.5 ms | 1.000 / 1.000 / 1.000 | 1619 / 1617 / 1615 |
| 20 ms | 20.000 / 20.000 / 20.007 | 20110 / 20120 / 20123 |

The engine's mean cannot tell the first three rows apart (it reads 0 for nearly every pass), and
reads 1.000 for the fourth, whose true mean is about 1.6 ms. The microsecond figure orders all five
and sits about 100 to 130 above the spin, which is what the idle row reads: the cost of a pass with
nothing in it, overshoot included. One window (1.22.3, 600 microseconds) held a 3 ms pass of the
engine's own, which is the 808. Repeating a window and comparing medians of means is the answer, as
for every figure here.

**Cost and limits.** The pump reads `Stopwatch.GetTimestamp` twice per pass on every host for the
whole of its life, and does the arithmetic only while a window is open. A pass that returns early
(`Suspended`, `Standby`) writes no engine sample, so its figure is the stopwatch time of that short
pass held inside the previous pass's millisecond; that already held for `Passes` and the median. A
platform whose sleep overshoots by a timer tick (Windows at its default resolution) was not
measured: there the clamp pins every pass to the top of the engine's millisecond, which is a
millisecond high at worst. `TickTimingTests` pins the relation to the engine's mean on a live
server (the microsecond mean sits in `[MeanMs * 1000, MeanMs * 1000 + 1000]`, at least the fixture's
20 ms spin, and above the engine's mean for an idle world); `PassTimingStatisticsTests` pins the
arithmetic, the clamp and the engine's float flooring of the sleep.
