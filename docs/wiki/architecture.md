# Architecture

Atlas has three layers: an engine that owns the embedded server and the game thread, an
xUnit adapter that discovers and schedules scenarios, and a bridge mod that hands the live
game API from inside the server to the engine.

This page condenses [docs/specs/2026-07-02-atlas-design.md](../specs/2026-07-02-atlas-design.md)
and [docs/feasibility-spike.md](../feasibility-spike.md); read those for the full rationale.

## Layers

```
 xUnit test runner
        |
        v
 +-------------------+     discovers [AtlasScenario], builds fixtures,
 | Atlas.XUnit        |     posts scenario delegates onto the game thread
 | (adapter)          |
 +-------------------+
        |
        v
 +-------------------+     owns the game thread, the tick pump, mod
 | Atlas / Internal   |     staging, the scheduler, the watchdog
 | (engine)           |
 +-------------------+
        |
        v
 +-------------------+     ModSystem loaded by the embedded server,
 | AtlasBridge        |     captures ICoreServerAPI, hands it to the
 | (bridge)           |     engine, drives Ticks/Until
 +-------------------+
        |
        v
   Vintage Story ServerMain (embedded, isDedicatedServer: false)
```

`Atlas` (the engine) does not depend on xUnit: bootstrap, mod staging, the scheduler and the
bridge rendezvous are runner-agnostic, so a future CLI (tracked as a `future:` issue) can
reuse them unchanged. Test authors only ever reference `Atlas.XUnit`.

## One live server per process

Vintage Story's embedding path relies on process-wide statics (`ServerMain.Logger`,
`GamePaths.DataPath`, `RuntimeEnv.ServerMainThreadId`, the global `TyronThreadPool.Inst`).
The feasibility spike proved sequential reuse is safe: many server lifecycles, one after
another, in the same process. Running two servers *concurrently* in one process is not
supported and is not attempted. This is why parallel scenario execution is out of scope for
v1 and requires multi-process orchestration instead (tracked as a `future:` issue).

## The game thread and the pump

Vintage Story expects a single thread to drive the server loop: whichever thread calls
`Launch()` becomes `RuntimeEnv.ServerMainThreadId`, and after that every `Process()` call
must come from that same thread. Atlas dedicates one thread per server instance to this
role:

1. Redirect `APP_CONTEXT_BASE_DIRECTORY` to the Vintage Story install and hook
   `AssemblyResolve` (install, install/Lib, install/Mods), so asset and library probing
   resolve correctly even though the host process is not the game executable.
2. Stage the mod(s) under test plus `AtlasBridge.dll` into a scratch mods folder.
3. Boot `ServerMain` with `isDedicatedServer: false` (no socket is ever opened) against a
   scratch data path, call `PreLaunch()` then `Launch()`.
4. Pump: call `Process()`, drain the scheduler's queue, repeat, until shutdown or a fatal
   error.

Because the game thread also drains the scheduler queue between `Process()` calls, scenario
code that runs on that queue has full, race-free access to the game API without any
additional locking.

## GameThreadScheduler

A custom `SynchronizationContext` installed on the game thread. The xUnit adapter posts each
scenario delegate into its queue and awaits completion; every `await` continuation inside a
scenario body returns to that same queue. Scenario code therefore never leaves the game
thread. `World.Ticks(n)` and `World.Until(...)` are continuations resumed by the bridge's
tick listener, not by the .NET thread pool. The scheduler itself is engine-agnostic and is
unit-tested against a fake pump, with no Vintage Story install required.

This is also why scenario bodies must never call `ConfigureAwait(false)`: doing so detaches
the continuation from the game thread's queue and breaks the thread-pinning guarantee. See
[writing-scenarios.md](writing-scenarios.md) for the full rule.

## AtlasBridge

A minimal server-side `ModSystem` shipped inside Atlas and staged as a dll next to the
mod-under-test. It captures `ICoreServerAPI` in `StartServerSide` and hands it to the engine
through a static rendezvous. This works because the game loads mod dlls into the default
`AssemblyLoadContext` from the staged path, and Atlas pre-loads the same file, so both sides
observe the same assembly identity and the same static state. `AtlasBridge` also registers
the tick listener that feeds `Ticks`/`Until`.

`ServerMain.api` is internal, so this bridge (rather than direct access from the host) is the
only way to reach `ICoreServerAPI`. It doubles as the natural place to host anything Atlas
needs to inject into the running server.

## World lifecycle

- One xUnit class fixture = one server, one fresh world, one scratch data path per test
  class. Scenarios in a class run sequentially against that world (the adapter disables
  xUnit's test parallelization for the assembly).
- `[AtlasScenario(FreshWorld = true)]` tears the class world down and reboots it before that
  scenario runs, for scenarios that pollute world state heavily.
- Cross-class isolation is total: fresh server, fresh world, fresh scratch path every time.

## Further reading

- [Design spec](../specs/2026-07-02-atlas-design.md): the full decisions table, error
  handling model, and out-of-scope list.
- [Feasibility spike](../feasibility-spike.md): the empirical groundwork (bootstrap gotchas,
  determinism evidence, prior art) that the engine is built on.
