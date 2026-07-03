# Writing scenarios

## Attribute reference

### `[assembly: AtlasMods(params string[] paths)]`

Assembly-level. Declares the mod(s) staged for every scenario class in the assembly. Paths
are resolved relative to the test assembly's own output directory, not the source tree.
Accepts a folder, a `.zip`, or a `.dll`, exactly what the game's own mod loader accepts.

### `[assembly: CollectionBehavior(DisableTestParallelization = true)]`

Standard xUnit attribute, required in every Atlas test assembly. Atlas hosts at most one
live server per process; scenario classes must run sequentially.

### `[AtlasWorld]` (class level)

Declares the world configuration a scenario class runs against. All properties are
optional:

| Property | Default | Meaning |
|---|---|---|
| `Seed` | `424242` | World seed. Identical seeds produce identical worlds. |
| `WorldType` | `"superflat"` | Type of world to create. |
| `PlayStyle` | `"creativebuilding"` | Play style for the world. |
| `Mods` | `[]` | Extra mod paths for this class, appended after the assembly-level ones. |

The defaults are deliberately fast and deterministic: superflat worldgen and a fixed seed
keep boot time low and results reproducible.

### `[AtlasScenario]` (method level)

Marks an `async Task` method as a scenario, discovered like `[Fact]` and run on the embedded
server's game thread.

| Property | Default | Meaning |
|---|---|---|
| `FreshWorld` | `false` | If true, the class host is recycled (server + world reboot) before this scenario runs, instead of reusing the world shared by the rest of the class. |
| `TimeoutMs` | `60000` | Maximum wall-clock time, in milliseconds, the scenario may run before an off-thread watchdog fails it and marks the class host dead. |

`TimeoutMs` deliberately does not reuse xUnit's own `[Fact(Timeout = ...)]`: xUnit posts its
timeout continuation back through `SynchronizationContext.Current`, which for an Atlas
scenario is the game thread's own queue. If the game thread itself is the thing that is
stuck, that continuation never drains and the test hangs forever instead of failing. Atlas
enforces `TimeoutMs` from an independent watchdog thread instead.

## Time model

Scenario code advances the world explicitly; nothing ticks on a wall-clock timer behind your
back except the server itself.

- `await World.Ticks(count)`: waits for `count` server ticks to elapse.
- `await World.Until(predicate, timeoutTicks = 600)`: polls `predicate` once per tick until
  it returns true, or throws `ScenarioTimeoutException` (carrying the number of ticks
  waited) once `timeoutTicks` elapses.

Both are **tick-based**, not wall-clock: they only make progress while the server is
actually ticking. This is different from `TimeoutMs` on `[AtlasScenario]`, which is
wall-clock and exists specifically to catch the case where ticking itself has stalled.

## World isolation

- One xUnit class fixture equals one server, one fresh world, one scratch data path, shared
  by every scenario in that class. **World state persists between scenarios of the same
  class** unless you opt out.
- Use `[AtlasScenario(FreshWorld = true)]` for a scenario that pollutes world state heavily
  (large builds, many spawned entities) and needs a clean slate rather than inheriting
  whatever earlier scenarios in the class left behind.
- Cross-class isolation is total: every test class gets its own server, world, and scratch
  path.

## The game thread rule

Every member of `IWorldSession` runs on the game thread. The xUnit adapter posts your
scenario delegate onto a `SynchronizationContext` installed on that thread, and every
`await` continuation inside the scenario body returns to that same queue by default. This is
what makes direct, unsynchronized calls into the Vintage Story API safe from scenario code.

**Never call `ConfigureAwait(false)` inside a scenario body.** Doing so detaches the
continuation from the game thread's queue and hands it to the .NET thread pool instead,
which breaks the thread-pinning guarantee: subsequent game API calls in that scenario would
run off-thread, racing the server's own pump. This is a contract, not something Atlas
detects or enforces at runtime, so it is on you as the scenario author to avoid it.

## Query and action surface

`AtlasScenarioBase.World` (an `IWorldSession`) is the entry point:

- Queries: `Spawn` (default spawn position, resolved to terrain height), `Calendar`,
  `BlockAt(pos)`, `BlockEntityAt<T>(pos)`, `EntitiesIn(area)`.
- Actions: `SetBlock(blockCode, pos)`, `SpawnEntity(entityCode, pos)`,
  `ExecuteCommand(command)` (any server console command, e.g. `"/time add 2"`, a large free
  lever for anything not directly modeled).
- Helpers: `BlockPos.Offset(dx, dy, dz)` and `BlockPos.Area(radius)` extension methods for
  building positions and cuboids around a reference point.

`EntitiesIn` currently only queries dimension 0; multi-dimension support is tracked as a
`future:` issue.

## Asserts

Atlas ships no custom assertion layer. Use standard xUnit `Assert` against whatever the
query surface returns.

## The `Api` escape hatch

`World.Api` exposes the raw `ICoreServerAPI` the embedded server is running. The query and
action surface above is deliberately small (YAGNI): anything Atlas does not model yet, reach
through `Api` directly, on the game thread, exactly like the rest of `IWorldSession`. Every
member of `IWorldSession`, including `Api`, is documented as running on the game thread; the
same rule applies to whatever you do through `Api`.

## Determinism notes

- Fixed seed plus superflat worldgen produces bit-identical worlds across runs, verified
  empirically during the feasibility spike.
- Tick *ordering* and resulting *world state* are reproducible because everything runs on a
  single game thread. Per-tick wall-clock timing inside a single `Process()` call is not
  bit-exact (it depends on `Config.TickTime` and per-system update intervals), so "wait N
  ticks" is reliable but "exactly N engine ticks of subsystem X fired in this window" is not
  guaranteed.
