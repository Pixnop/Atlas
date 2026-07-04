# Changelog
All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-07-04

### Added

- Headless test players (issue #4, single-player-slot scope): `IWorldSession.JoinPlayer(name)`
  joins a real, world-present `EntityPlayer` over the same in-memory dummy-network mechanism the
  game's own singleplayer client uses, bypassing auth the same way singleplayer does. Returns
  `ITestPlayer`: `Entity`/`Player` escape hatches, `Position`, `Stats`, `GiveItem(code, quantity)`
  (into the active hotbar slot, with quantity and stack-size validation), and `TeleportTo(pos)`
  (dimension-aware; completes only once the engine's chunk-load-dependent teleport has actually
  applied, via the engine's own teleport callback). A rejected or timed-out join surfaces as an
  actionable `AtlasSetupException` (naming the join step and likely causes) and releases the
  player slot for a retry. A second `JoinPlayer` on the same world throws `AtlasSetupException`
  naming the remedies (`FreshWorld = true`, or join once and share the player): the dummy-network
  mechanism claims a single, fixed-size socket slot on the embedded server; concurrent multiple
  test players needs its own spike to multiplex several dummy connections into that slot.
- `IEntityStats` + `IWorldSession.StatsOf(entity)`: read-only `Health`, `MaxHealth`, `Saturation`,
  and a generic typed `Attribute<T>(path)` reader over an entity's watched-attribute tree, for any
  entity (not just players).

### Fixed

- (Internal, discovered while adding `JoinPlayer`) A headless test player's dummy UDP connection
  never sends real UDP traffic, which the engine's own delayed UDP-check background task
  interprets as "client never sent UDP" and reacts to accordingly; on a short-lived scenario, that
  reaction could race the scenario's own teardown and crash the test process from a thread Atlas
  does not control. Marking the connection's `ServerDidReceiveUdp` flag once, right after join,
  avoids the reaction entirely.
- The `<AtlasMod>true</AtlasMod>` ProjectReference sugar now stages folder mods correctly.
  `WriteAtlasModManifest` used to always write the tagged reference's bare resolved dll path,
  which only works for mods carrying an assembly-level `ModInfo` attribute; mods discovered via
  a `modinfo.json` copied next to the dll (no assembly attribute - the common csproj layout, and
  Manifold's case) were rejected by the game's ModLoader with a confusing "no ModInfo attribute"
  error even though the dll had a ModSystem. The target now checks for a sibling `modinfo.json`
  next to each tagged reference's resolved output and writes that reference's output directory
  instead of the dll path when found.
- `ModStager.Stage` (and `StageBridge`) now trim trailing directory separators before deriving
  the staged file/folder name. A path with a trailing separator made `Path.GetFileName` return
  an empty string, which silently flattened the source folder's contents straight into the
  staging root instead of nesting them under a mod folder. A path that still yields an empty
  name after trimming now throws `AtlasSetupException` naming the offending path, instead of
  flattening silently.

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
