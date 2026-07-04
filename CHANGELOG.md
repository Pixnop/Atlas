# Changelog
All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-07-04

### Added

- Project wiki: the full documentation (getting started, writing scenarios, mod staging,
  architecture, CI recipes, compatibility, troubleshooting, roadmap) now lives on the
  [GitHub wiki](https://github.com/Pixnop/Atlas/wiki). The in-repo `docs/wiki/*.md` files are
  retired to pointer stubs.
- `WorldArea`: a `Cuboidi` paired with the dimension it lives in, with an implicit conversion
  back to `Cuboidi` for call sites that only need the bounds.
- `IWorldSession.EntitiesIn(WorldArea)`: dimension-aware entity query, using the dimension
  carried by the area instead of always querying dimension 0.
- MSBuild staging sugar: a test project can reference its mod-under-test as an ordinary
  `ProjectReference` tagged `<AtlasMod>true</AtlasMod>` instead of hand-writing a relative
  path into `[assembly: AtlasMods(...)]`. `build/Atlas.E2E.targets` (shipped as
  `buildTransitive` by `Pixnop.Atlas.XUnit`) writes the resolved output path of every tagged
  reference into `atlas-mods.generated.txt` next to the test assembly at build time;
  `AttributeMapper` reads it when present and appends its paths after the ones declared via
  attributes.

### Changed

- `BlockPosExtensions.Area(radius)` now returns a `WorldArea` (inheriting the source
  `BlockPos`'s dimension) instead of a bare `Cuboidi`. `IWorldSession.EntitiesIn(Cuboidi)` is
  kept, documented as dimension 0, and implemented as `EntitiesIn(new WorldArea(area, 0))`.

### Fixed

- Staging `AtlasBridge.dll` into the embedded server's mod folder surfaced file system
  failures (locked file, permissions, missing source) as an opaque host crash. The copy now
  rethrows as `AtlasSetupException`, naming the source and destination paths and carrying
  the file system error as the inner exception, so setup failures read as setup failures.
- When `server.Stop()` itself threw during teardown after a game-thread crash, the original
  crash was wrapped in an `AggregateException(original, stopFailure)`, burying the root
  cause. The original exception is now kept as the sole crash (one level deep under
  `ServerCrashedException`, the same shape as every other crash) and the stop failure is
  logged to stderr instead.
- `IWorldSession.SpawnEntity` silently spawned every entity in dimension 0, whatever the
  given `BlockPos`'s dimension: the engine's `EntityPos.SetPos(BlockPos)` does not propagate
  dimension on its own. Entities now spawn in the dimension of the position they are given.

## [0.1.0] - 2026-07-03

### Added

- NuGet packages: `Pixnop.Atlas.Bridge`, `Pixnop.Atlas`, and `Pixnop.Atlas.XUnit`
  (dependency chain Bridge <- Atlas <- Atlas.XUnit). `Pixnop.Atlas.XUnit` ships
  `build/Atlas.E2E.targets` as a `buildTransitive` MSBuild target, so out-of-repo consumers
  get the Newtonsoft.Json shadowing fix and the `VintageStoryPath` environment fallback
  automatically, with no manual relative `<Import>`.

- In-process embedded Vintage Story server engine (`Atlas`): game-thread bootstrap and
  pump, `AssemblyResolve` hooking against a `VINTAGE_STORY` install, scratch data path per
  server instance.
- `Atlas.Bridge`: harness `ModSystem` capturing `ICoreServerAPI` and rendezvousing it with
  the engine across the mod loader's assembly load context.
- Mod staging (`ModStager`): folder, `.zip`, and `.dll` mod paths resolved relative to the
  test assembly output directory, staged alongside `Atlas.Bridge.dll`.
- `GameThreadScheduler`: custom `SynchronizationContext` pinning scenario code and its
  `await` continuations to the game thread.
- `IWorldSession` author-facing API: `Spawn`, `Calendar`, `BlockAt`, `BlockEntityAt<T>`,
  `EntitiesIn` (dimension 0), `SetBlock`, `SpawnEntity`, `ExecuteCommand`, `Ticks`, `Until`,
  and the raw `Api` escape hatch.
- `BlockPosExtensions`: `Offset` and `Area` helpers for building positions and cuboids.
- Exception types: `AtlasSetupException`, `ScenarioTimeoutException`,
  `ServerCrashedException`.
- `Atlas.XUnit` adapter: `[AtlasScenario]`, `[AtlasWorld]`, `[assembly: AtlasMods(...)]`,
  `AtlasScenarioBase`, a custom xUnit test case discoverer/runner/invoker, and an
  off-thread `Watchdog` enforcing `TimeoutMs` independently of xUnit's own timeout path.
- World lifecycle: one server and world per test class fixture, sequential scenario
  execution within a class, `FreshWorld = true` per-scenario opt-out, fixed seed (424242)
  and superflat/creative defaults.
- `samples/SampleMod` and `samples/Sample.Scenarios`: end-to-end sample mod and scenarios
  compiling against `Atlas.XUnit` alone, doubling as living documentation.
- `build/Atlas.E2E.targets`: shared MSBuild fix copying the game's own `Newtonsoft.Json.dll`
  over the test SDK's transitive copy for E2E consumers.
- `Atlas.Pure.Tests`: unit tests of the engine's pure logic (scheduler, ticks, mod staging,
  attribute mapping, watchdog) against a fake pump, no Vintage Story install required.
- `Atlas.Engine.Tests`: E2E smoke tests against a real embedded server.
- CI (`ci.yml`): build and pure-test job on every push/PR, plus an E2E job that downloads
  the Vintage Story server and runs the sample scenarios.
- README, wiki documentation (architecture, getting started, writing scenarios), and this
  changelog.
