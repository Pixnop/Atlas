# The tick contract: World.Ticks(n), engine passes, and real simulation ticks

Date: 2026-07-14
Status: measured and implemented (this pass): contract documented, `EntitySimulationTicks`
counter shipped; `Ticks(n)` semantics deliberately unchanged
Tracks: issue #79 "Document the Ticks(n) to simulation-ticks contract (or expose a real
server-tick counter)", from the StratumParity field report
Game versions measured: 1.22.3 as the reference (decompiled and run live), 1.21.7 and
1.20.12 (decompiled, targeted); Stratum v1.22.3-stratum patches read at source, its
install run live; all live runs instrumented through a temporary in-repo probe scenario
Prerequisites: [Atlas design](2026-07-02-atlas-design.md)

## Motivation

StratumParity (differential suite, vanilla 1.22.3 vs the Stratum fork, Atlas 0.8.0)
reported that entity-tick-frequency probes - a counting `EntityBehavior` on spawned straw
dummies - observed non-constant ratios between `World.Ticks(n)` and actual entity ticks:
a probe placed 5 blocks from world spawn ("near band" by intent) sometimes counted about
half of `Ticks(n)`, sometimes 100 percent, varying between engine flavors and between
runs of the same flavor. The suite had to fall back to ratio assertions. Atlas had never
written down what a tick awaited through the harness actually guarantees about simulation
progress, so there was no way to tell whether the variance was an Atlas artifact, an
engine property, or a probe bug. This pass measured all three and answers with: an
explicit contract for `Ticks(n)`, the root cause of the field observation (it is not an
Atlas pump artifact), and a real simulation-tick counter for exact assertions.

## Method

- Decompilation (ILSpy) of `ServerMain.Process()`, `Vintagestory.Common.EventManager`
  (`TriggerGameTick`, `GameTickListener`), `ServerSystem`,
  `ServerSystemEntitySimulation` and `ServerConfig` on 1.22.3, 1.21.7 and 1.20.12.
- Source reading of the Stratum fork's patches to the same types
  (`patches/VintagestoryLib/Vintagestory.Server/`, local clone of StratumServer/Stratum),
  plus its `StratumConfig` defaults, to cover the field report's second flavor.
- Live instrumented runs (temporary probe scenario, deleted after measurement): a joined
  test player, a straw dummy with a counting behavior, a 1ms game-tick listener sampling
  `World.ElapsedMilliseconds` and the entity-simulation system's `millisecondsSinceStart`
  per pump pass, over `Ticks(150)` windows, against vanilla 1.22.3 and a bootstrapped
  Stratum install.

## The engine's tick machinery, as measured

One `ServerMain.Process()` call is one server "tick" in the engine's own vocabulary (its
stats count `ticksTotal++` per call; its overload warning says "A tick took {n}ms"). Per
pass, in the run phase (1.22.3 and 1.21.7, structurally identical):

1. One snapshot of the master clock: `elapsedMilliseconds =
   totalUnpausedTime.ElapsedMilliseconds` (the stopwatch behind
   `IWorldAccessor.ElapsedMilliseconds`; it pauses while the server is suspended).
2. Server systems, each at most once per pass, gated by its own stride:
   `if (elapsed - system.millisecondsSinceStart > system.GetUpdateInterval())` then stamp
   `millisecondsSinceStart = elapsed` and run `OnServerTick`. Entity simulation
   (`ServerSystemEntitySimulation`, whose `OnServerTick` -> `TickEntities` calls
   `Entity.OnGameTick` on every loaded, tickable-dimension entity) has
   `GetUpdateInterval() == 20` (ms) on all three versions.
3. Game-tick listeners, via `EventManager.TriggerGameTick(elapsed, ...)`, once per pass:
   each listener fires when `elapsed - listener.LastUpdateMilliseconds >
   listener.Millisecondinterval`, then stamps itself with the same pass snapshot.
4. Pacing: sleep `max(0, Config.TickTime - passDuration)`. `TickTime` defaults to
   `33.333332f` (30 TPS) on all three versions, and Atlas worlds boot on a fresh
   serverconfig, so pump passes are at least ~33ms apart in the steady state; a pass
   whose work exceeds the budget just starts the next one immediately (no catch-up
   burst, no multi-tick pass).

1.20.12 differs only in bookkeeping: it schedules against a projected clock
(`elapsed + expectedSleep`) and computes the sleep from the previous frame's duration.
The stride mechanism, the 20ms entity-simulation interval, the strictly-greater-than
comparisons, and the at-most-once-per-pass property are identical.

Two structural facts fall out of the decompiles:

- **The engine keeps no monotonic tick counter.** `StatsCollection.ticksTotal` is a
  2-second rolling window (reset on rotation), `ServerMain.TickPosition` is a per-pass
  debug position marker (reset to 0 every pass). The only authoritative, monotonic
  record of simulation progress is the master clock plus each consumer's own
  last-fire stamp.
- **"Ticks" are per-consumer strides over one shared clock, not a shared step.** Every
  consumer (each server system, each tick listener) fires when the clock moved past its
  own interval since its own last fire. Under the default 33.33ms pacing every consumer
  with an interval below ~33ms fires on every pass, which is why the ecosystem talks
  about "the server tick" as if it were one thing - but nothing in the engine promises
  that alignment, and forks add per-entity strides on top (below).

## What one Atlas tick is, and what Ticks(n) guarantees

The Atlas bridge mod registers `api.Event.RegisterGameTickListener(onTick, 1)`; each fire
raises `TickSource.RaiseTick`, which advances waiters. So:

- One Atlas tick = one fire of a 1ms-interval game-tick listener = **at most one per
  `ServerMain.Process()` pass**, and only when more than 1ms of unpaused master clock
  passed since the previous fire.
- `await World.Ticks(n)` therefore guarantees: **n engine passes in which the game-tick
  section ran, each pass paced at `Config.TickTime` (~33.33ms on a default config) in
  the steady state, completed before your continuation runs.** The continuation resumes
  on the game thread after the pass that completed the wait (pump order: `Process()`,
  then counter sample, then scheduler drain).
- `Ticks(n)` does **not** guarantee: n entity-simulation ticks for any particular entity
  (per-system stride, per-entity throttling in forks); n fires of any other tick
  listener or block/system stride; any fixed wall-clock duration (suspend pauses the
  clock; overloaded passes run long); or that a tick "happened" for consumers whose
  stride exceeds the pass cadence.

On the supported vanilla engines under default pacing, Atlas ticks, engine passes and
entity-simulation ticks all run 1:1 (each ~33ms pass satisfies both the 1ms listener
stride and the 20ms entity stride) - measured live: 150 Atlas ticks over 4997ms with
148-150 entity-simulation fires, cadence histogram centered on 33-34ms. That 1:1 is an
emergent property of the default numbers, not a contract.

## Why the field probes saw "about half, sometimes 100 percent"

Not an Atlas artifact, and not loop pacing: measured on the Stratum install, the pump
cadence (33-34ms) and the entity-simulation system's fire rate (148-150 per 150 Atlas
ticks) are identical to vanilla; Stratum's patches leave `Process()` pacing, the 20ms
entity stride and the listener dispatch untouched. The halving is Stratum's
**per-entity distance-band throttle** interacting with the engine's **randomized player
spawn**:

- Stratum (`Performance.EntityTicking`, on by default) ticks each stationary entity at a
  stride chosen by its distance to the nearest playing client: within 32 blocks every
  entity-simulation tick, within 64 every 2nd, within 96 every 5th, beyond every 10th.
- The engine spawns each new player at a **randomized position within the world's
  `spawnRadius`** of the default spawn (`SpawnPlayerRandomlyAround` ->
  `LocateRandomPosition`, triggered for new player entities when `spawnRadius > 0`;
  the default playstyle worlds Atlas boots carry `spawnRadius = 50`, measured in the
  live world config).
- The field probes anchored distance to **world spawn** ("5 blocks away"), but the band
  is measured from the **player**. The anchor player lands anywhere within 50 blocks of
  world spawn, so a probe at world-spawn+5 sits at a random 0-55 blocks from the player:
  under 32 on some runs (near band, full rate: "100 percent"), over it on others (mid
  band, every 2nd tick: "about half").

Reproduced live, three consecutive Stratum runs of the same probe (150-tick windows):

| Run | Player-to-probe distance | Entity ticks / Atlas ticks | Band |
|---|---|---|---|
| 1 | 25.5 blocks | 150/150 | near (interval 1) |
| 2 | 50.8 blocks | 75/150 | mid (interval 2) |
| 3 | 35.5 blocks | 75/150 | mid (interval 2) |

Vanilla has no banding, so the same probe measures 150/150 regardless of where the
player lands - which is exactly the field report's "varying between engine flavors and
even between runs". Probe-author guidance that falls out: anchor probe distances to the
**player entity's actual position** (`player.Entity.Pos`), never to world spawn.

## EntitySimulationTicks: the real counter (remedy 2)

`IWorldSession.EntitySimulationTicks` (long, monotonic, game-thread) counts the embedded
server's real entity-simulation ticks, so frequency probes assert exact counts:

```csharp
long before = World.EntitySimulationTicks;
await World.Ticks(150);
long simTicks = World.EntitySimulationTicks - before;   // exact, not a ratio
Assert.Equal(simTicks, probe.Ticks);                     // unthrottled entity, any flavor
```

Mechanism: the engine's own record of the system's last tick is the authoritative
signal - `ServerSystem.millisecondsSinceStart` (public on every supported version) of
the `ServerSystemEntitySimulation` entry in the internal `ServerMain.Systems` array
(reflected once per host; `FieldInfo`s cached per process). The game-thread pump samples
it after every `Process()` pass, from the first pass of the boot onward, and counts a
tick when the stamp moved. The sampling is exact, not approximate: the engine ticks each
system at most once per pass, and a fire strictly increases the stamp (the fire condition
requires the clock to have advanced past the 20ms stride), so one sample per pass sees
every fire exactly once. The counter is sampled before the scheduler drain, so a
continuation resumed on a pass already sees that pass's tick counted; reads and a
counting probe's own increments therefore align window-for-window, which is what makes
`Assert.Equal` (rather than a bounded ratio) correct.

Engine symbols relied on, verified by decompile on 1.20.12, 1.21.7 and 1.22.3 and
against the Stratum patches (which touch none of them):

| Symbol | Shape | Role |
|---|---|---|
| `ServerMain.Systems` | `internal ServerSystem[]`, built by `Launch()` | reflected once per host, before the first `Process()` pass |
| `ServerSystemEntitySimulation` | `public class`, one entry in `Systems` | matched by type name, base-type chain included (fork subclasses) |
| `ServerSystem.millisecondsSinceStart` | `public long`, stamped per fire | the sampled tick signal |
| `ServerSystemEntitySimulation.GetUpdateInterval()` | `20` (ms) on all three | why 1:1 with passes holds at default pacing |

Drift behavior: resolution failure at boot degrades the counter (one-time stderr warning
naming the symbols, the `ServerAssetsBuildProbe` pattern); the host boots and every other
API works. Only reading `EntitySimulationTicks` then throws `AtlasSetupException` with
the same symbol list - a wrong count is worse than a loud absence. The resolution rules
and the tick decision are pure (`SimulationTickSignal`, unit-tested); the live reads live
in the thin `EntitySimulationTickCounter` shell.

## Ticks(n) itself is deliberately unchanged

`Ticks(n)` could be made to await n real entity-simulation ticks cheaply (complete
waiters from the counter's sample step instead of the bridge listener), but existing
suites calibrated timeouts and waits against pump-pass ticks, and on default-paced
vanilla engines the two are indistinguishable anyway. Repointing the semantics is left
as a possible follow-up (tracked in the issue #79 PR discussion), to be taken only with
a field case that pump-pass ticks cannot serve.
