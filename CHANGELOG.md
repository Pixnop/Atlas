# Changelog
All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- World rollback (issue #2, stage 1): `[AtlasScenario(RollbackWorld = true)]` restores the class
  host's world to its snapshot before the scenario runs, in the same lifecycle slot where
  `FreshWorld = true` recycles the whole host today, but without rebooting the server (measured
  at roughly 25x faster than a recycle on the baseline superflat world). The snapshot is
  captured lazily, once per host, at the class's first rollback-enabled scenario, so classes
  that never opt in pay nothing. A rollback restores blocks, block entities, chunk-stored
  entities, chunk moddata, savegame data and the calendar (dimension 0); it does NOT restore mod
  in-memory state that ignores chunk/entity lifecycle events, nor in-memory map chunk state
  (height maps, map moddata). Fail closed: if capture or restore fails for any reason (including
  engine internals drifting in a future game version), Atlas logs a one-line warning to stderr
  and falls back to the full host recycle, so the scenario still gets its clean world. Guard
  rails: requesting a rollback on a class that has joined test players fails the scenario with
  `AtlasSetupException` (player state would not be rolled back; players + rollback is a later
  stage), and combining `RollbackWorld` with `FreshWorld` is a setup error.

- `atlas run --worker`: worker execution mode for the CLI, stage 1 of the multi-process
  parallelization design (issue #1). `--worker` runs the assembly exactly like plain `run`
  (one process, sequential, same exit codes) but reports exclusively as line-delimited JSON
  events on stdout: `run-start`, `class-start`, `test-pass`/`test-fail`/`test-skip`,
  `class-end`, `error`, `run-end`, every line versioned with `v: 1`. All human and engine
  chatter (the embedded server logs to the console) is rerouted to stderr, and a fail-safe
  guarantees the stream always ends with a well-formed `run-end` even when the run crashes.
  `--classes <A,B>` (worker mode only; a usage error without `--worker`) restricts the run to
  the given scenario classes by exact fully qualified name, and `--list --worker` emits one
  `discovered` event per scenario without booting anything: together they are the seam the
  stage 2 orchestrator (`--parallel N`) will drive. The protocol contract is documented in
  docs/specs/2026-07-06-worker-protocol.md.

## [0.5.0] - 2026-07-06

### Added

- `atlas run` CLI facade (issue #3): a `dotnet tool` (package `Pixnop.Atlas.Cli`, command
  `atlas`) that executes the Atlas scenarios of a compiled test assembly without VSTest,
  through the same in-process xunit runner the engine's own nested E2E tests use. One process,
  sequential, embedded server booted exactly as under `dotnet test`. `atlas run
  path/to/Scenarios.dll` streams per-scenario PASS/FAIL lines with durations and a summary,
  and exits non-zero on any failure (an empty run counts as a failure, so a typo'd filter
  cannot go green in CI); `--filter <substring>` selects scenarios by display name (ordinal,
  case-insensitive); `--list` prints the discovered scenarios without booting anything.
  `VINTAGE_STORY` is validated up front with the same check as the engine's boot, so a missing
  install fails fast at the CLI boundary. Building block for future multi-process
  parallelization (issue #1).

- Prebuilt world saves: `[AtlasWorld(SaveFile = "fixtures/myworld.vcdbs")]` (or
  `WorldOptions.SaveFile`) boots the scenario class against a copy of the given save instead of
  generating a fresh world. Any file name works (the copy is renamed to the engine's pinned save
  name), the fixture itself is never written to, and each test class gets its own pristine copy,
  so tests cannot corrupt the fixture or each other. `Seed`, `WorldType` and `PlayStyle` are
  ignored when a save is supplied; the savegame carries its own world configuration. A missing
  fixture fails the boot with an `AtlasSetupException` naming the path.

- `ITestPlayer.IsConnected`: first-class "was the player dropped by the server" signal. `false`
  once the server has removed the player (kick, ban); test players never leave on their own, so
  a `false` value always means the server ended the connection. Kicks issued from a background
  thread settle a few ticks late (see the zombie-kick fix below), so wait with
  `await world.Until(() => !player.IsConnected)` rather than asserting right after the kick.

### Fixed

- Kicked test players no longer linger as zombies (kick-on-join left the player in
  `AllOnlinePlayers` with `ConnectionState == Admitted`, a still-ticking half-despawned entity,
  and per-tick "Exception thrown while calculating near heat source strength" warnings). Root
  cause: mods that kick from a thread-pool thread (e.g. after an HTTP check inside a PlayerJoin
  handler, the Nimbus.ServerMod pattern) crash the engine's own teardown -
  `ServerMain.FrameProfiler` is `[ThreadStatic]`, so off the game thread `DespawnEntity` dies on
  a `NullReferenceException` after the PlayerDisconnect event fired but before the client and
  entity registries were cleaned - and the kicking mod's own `catch` usually swallows the crash.
  A real TCP client self-heals because its socket close re-runs the teardown on the game thread;
  Atlas's dummy socket has no close semantics, so Atlas now supplies that second run itself
  (`KickedPlayerCleanup`): when a dropped test player is still registered, the teardown is
  re-run on the game thread, the player's TCP socket slot is released, and the joined-name claim
  is freed so the scenario can rejoin under the same name.

- Missing-pdb preflight: a `VintagestoryAPI.dll` copied into the test output without its
  `VintagestoryAPI.pdb` used to kill every scenario at server boot with an opaque
  `TypeInitializationException` (`NullReferenceException` in `LoggerBase..cctor` - the game's
  logger derives source paths from pdb debug info in its static constructor). The boot now fails
  fast with an `AtlasSetupException` naming the directory and the fix, and `Atlas.E2E.targets`
  additionally warns at build time when the dll lands in the output without its pdb.

- Pre-boot data path seeding: `[AtlasDataFiles(...)]` (assembly- or class-level, repeatable)
  copies fixture files or directory trees into the embedded server's scratch data path before
  `ServerMain` launches, so mods that read their config once in `StartServerSide` via
  `api.LoadModConfig` see the seeded file instead of booting unconfigured. Point a fixture folder
  at a data-path subfolder (`[AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]`)
  or lay the fixture tree out like the data path and overlay it onto the root
  (`[AtlasDataFiles("fixtures/serverdata")]`). Assembly-level seeds apply first, then
  class-level, so class-level files win on a name collision; missing sources and target paths
  escaping the data path fail the boot with `AtlasSetupException`. `samples/SampleConfigMod` plus
  `ConfigScenarios` in `samples/Sample.Scenarios` demonstrate the end-to-end pattern.

## [0.4.1] - 2026-07-04

### Fixed

- The embedded server now pins the process current directory to the Vintage Story install at
  boot (issue #32). The engine's mod loader scans mod dlls with Mono.Cecil's default assembly
  resolver, whose search path is the current directory: a test run launched from a directory
  holding no `VintagestoryAPI.dll` copy failed every base-game mod's ModInfo scan, loaded zero
  mod systems, and crashed the boot in `selectPlayStyle`. Atlas resolves consumer mod paths
  against the test assembly's location, never the current directory, so nothing else observes
  the change.

## [0.4.0] - 2026-07-04

### Added

- Concurrent test players (issue #26): `JoinPlayer` can now be called multiple times on the same
  world, one call per distinct player name, and the joined players coexist and act independently
  (own connection, own inventory). Internally, each player rides its own dummy TCP socket: the
  engine's packet loop iterates whatever socket array is installed, so Atlas grows the array by
  one slot per player instead of multiplexing one slot; the single dummy UDP server the engine
  hard-wires every singleplayer-type client to is shared. Joining twice under the same name still
  throws `AtlasSetupException` up front - the server would treat the duplicate as the same
  account reconnecting and kick the first player mid-scenario.

### Changed

- Breaking: `IWorldSession.ExecuteCommand` now returns `Task<CommandResult>` instead of `void`
  (issue #25). `CommandResult` carries `Ok`, the localization-resolved `Message`, and the
  engine's raw `TextCommandResult` as an escape hatch. Commands run as the engine's own console
  caller (admin role, every privilege); deferred (async-parsing) commands are followed to their
  final result; an unknown command completes with `Ok = false` instead of throwing, so scenarios
  can assert on intentional failures. Scenarios that drove a fixture mod through commands no
  longer need the SaveGame side channel to read outcomes, and a slashless command now throws
  `ArgumentException` instead of being silently misparsed by the engine's dispatch.

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
