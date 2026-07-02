# Atlas design: test-authoring surface and engine

Date: 2026-07-02
Status: approved (brainstorming Phase 1)
Prerequisite: [feasibility spike](../feasibility-spike.md) (GO - in-process embedded server,
runner thread = game thread)

## Goal

Atlas lets a mod author write deterministic integration scenarios against a real headless
Vintage Story server, run them with `dotnet test`, and get CI-ready results. Atlas is
generic: any VS mod is testable, Manifold and Chart are just the first consumers.

## Decisions (locked in this phase)

| Topic | Decision |
|---|---|
| Scenario authoring | Attributes + async/await (`[AtlasScenario] async Task`) |
| Runner | xUnit / `dotnet test` integration; no custom CLI in v1 |
| Time model | `await World.Ticks(n)` and `await World.Until(predicate, timeoutTicks)` |
| World lifecycle | One fresh server+world per test class (fixture); `FreshWorld = true` opt-out per scenario |
| World defaults | Superflat, creative playstyle, fixed seed; overridable via `[AtlasWorld]` |
| Mod-under-test | Code-first paths (assembly/class attribute), accepting folder, zip or dll |
| Asserts | Rich query surface + standard xUnit `Assert`; no custom assertion layer |
| Reporting | Native `dotnet test` loggers (TRX etc.); nothing custom |

## Project layout

```
src/Atlas/            Atlas.Api.* (public surface) + Atlas.Internal.* (engine)
src/Atlas.XUnit/      xUnit adapter: [AtlasScenario], fixtures, game-thread scheduler glue
tests/Atlas.Pure.Tests/   pure unit tests of Atlas itself (no VS)
samples/SampleMod/    trivial mod + example scenarios (end-to-end proof of the harness)
```

- `Atlas` does not depend on xUnit: the engine (bootstrap, staging, scheduler, bridge) is
  runner-agnostic so a future CLI reuses it unchanged.
- Test authors reference `Atlas.XUnit` only.
- TFM `net10.0`, compile-time ref `VintagestoryAPI.dll` via `%VINTAGE_STORY%` (same pattern
  as Manifold), minimum game version 1.22.x, semver 0.x until the API settles.

## Author-facing surface

A complete test:

```csharp
[assembly: AtlasMods("mods/manifold_0.4.1.zip")]           // mod(s) under test

[AtlasWorld(Seed = 424242)]                                 // defaults: superflat, creative
public class TransitScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Item_survives_dimension_transit()
    {
        var pos = World.Spawn.Offset(2, 0, 0);
        World.SetBlock("game:chest-east", pos);             // direct call: we ARE the game thread
        await World.Ticks(20);
        Assert.Equal("game:chest-east", World.BlockAt(pos).Code.ToString());
    }

    [AtlasScenario(FreshWorld = true)]                      // heavy pollution: fresh world
    public async Task Dimension_creation_is_isolated()
    {
        await World.Until(() => World.EntitiesIn(World.Spawn.Area(8)).Any(), timeoutTicks: 200);
    }
}
```

### Attributes

- `[assembly: AtlasMods(params string[] paths)]` - mods staged for every class in the
  assembly. Paths resolve relative to the test assembly directory; folder, `.zip` and
  `.dll` accepted (exactly what the game's ModLoader accepts).
- `[AtlasWorld]` (class level) - `Seed`, `WorldType`, `PlayStyle`, `Mods` (extra per-class
  mods), all optional with deterministic defaults (seed `424242`, superflat, creative).
- `[AtlasScenario]` (method level, `async Task`) - discovered like `[Fact]`;
  `FreshWorld = true` recreates server+world for that scenario; `TimeoutMs` overrides the
  per-scenario watchdog (default 60 s).

### IWorldSession (property `World` on `AtlasScenarioBase`)

- Queries: `BlockAt(pos)`, `BlockEntityAt<T>(pos)`, `EntitiesIn(area)`, `Calendar`, `Spawn`.
- Actions: `SetBlock(code, pos)`, `SpawnEntity(code, pos)`, `SpawnItemStack(...)`,
  `ExecuteCommand("/time set day")` (server console commands - large free lever).
- Time: `await Ticks(n)`, `await Until(predicate, timeoutTicks)` (throws
  `ScenarioTimeoutException` with tick and predicate context).
- Escape hatch: `Api` exposes the raw `ICoreServerAPI`. The Atlas query/action surface
  deliberately starts small (YAGNI); anything Atlas cannot do yet, the author does through
  the game API directly. Everything is documented "runs on the game thread".

## Engine internals

### Game thread and pump

One dedicated thread per server instance, owned by Atlas (`Atlas.Internal`):

1. Redirect `APP_CONTEXT_BASE_DIRECTORY` to the VS install; hook `AssemblyResolve`
   (install, install/Lib, install/Mods) - spike recipe.
2. Stage mods into a scratch folder: mod(s)-under-test + `AtlasBridge.dll`.
3. Boot `ServerMain` (`isDedicatedServer: false`, so no network socket is ever opened,
   scratch `DataPath`, `StartServerArgs` from `[AtlasWorld]`), `PreLaunch()`, `Launch()`.
4. Pump: `Process()` -> drain scheduler queue -> repeat, until shutdown or fatal error.

### GameThreadScheduler (the single marshalling component)

A custom `SynchronizationContext` installed on the game thread. The xUnit adapter posts the
scenario delegate into its queue and awaits completion; every `await` continuation inside
the scenario returns to that queue. Scenario code therefore never leaves the game thread -
VS API access is race-free by construction. `Ticks(n)` / `Until(...)` are continuations
resumed by the bridge's tick listener. The scheduler is engine-agnostic and unit-tested
against a fake pump (no VS).

### AtlasBridge (harness mod)

A minimal server-side `ModSystem` shipped inside Atlas, staged as a dll next to the
mod-under-test. It captures `ICoreServerAPI` in `StartServerSide` and hands it to the engine
through a static rendezvous (safe: the game loads mod dlls into the default
AssemblyLoadContext from the staged path; Atlas pre-loads the same file so both sides see
the same assembly instance). It also registers the tick listener that feeds `Ticks`/`Until`.

### World lifecycle

- xUnit class fixture = one server + fresh world + scratch data path per test class;
  scenarios in a class run sequentially against it (xUnit parallelism disabled by the
  adapter).
- `FreshWorld = true` tears down and reboots within the class.
- Cross-class isolation is total (fresh everything; proven repeatable by the spike).
- In-process constraint (engine statics): never more than one live server per process.

## Error handling

- **Scenario timeout**: watchdog per scenario (default 60 s); `ScenarioTimeoutException`
  carries current tick and what was awaited. A hung scenario fails; the suite never freezes.
- **Server crash mid-scenario**: the exception is captured on the game thread and rethrown
  inside the owning xUnit test; the class world is marked dead and remaining scenarios in
  the class fail fast with a clear message (no cascading opaque timeouts).
- **Mod load failure**: fixture startup fails with the ModLoader report (mods found, sort
  order, errors) instead of a downstream timeout.
- **Diagnostics**: server logs land in the scenario's scratch data path; the path is printed
  on failure.

## Testing Atlas itself

- `Atlas.Pure.Tests` (xUnit + NSubstitute, no VS): scheduler ordering/timeout/exception
  semantics against a fake pump, mod path resolution, staging, attribute discovery,
  `StartServerArgs` mapping. Target 80%+ on `Atlas.Api.*` / `Atlas.Internal.*` pure logic.
- `samples/SampleMod` + example scenarios: the end-to-end proof, doubling as documentation.
  Phase 2's smoke milestone (boot, 1 tick, 1 assert, stop) is its first scenario.

## CI

`ci.yml`: build + pure tests on push/PR; an e2e job downloads the VS server (as Manifold's
`package.yml` does) and runs the sample scenarios. Output: standard `dotnet test` TRX.

## Out of scope for v1 (tracked as issues, not silent TODOs)

- Parallel scenarios (multi-process orchestration)
- World snapshot/rollback for faster isolation
- `atlas run` CLI facade
- Simulated player connections (client-side)
- MSBuild staging sugar (`ProjectReference` auto-discovery)
- Any transport layer / MCP (separate future project)
