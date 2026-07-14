# Changelog
All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `JoinPlayer` no longer hands the world back to the scenario while the engine's background
  server-assets build can still be enumerating live game content (issue #84, from the
  StratumParity field report: a scenario that joined a player and immediately ran a
  2048-SetBlock burst, under a staged source mod that lengthens the build, hit "Collection was
  modified" inside `BuildServerAssetsPacket` on a TyronThreadPool thread, and an unhandled
  pool-thread exception kills the whole testhost process, twice in a row on a 4-core CI
  runner). After the join reaches the Playing state, `JoinPlayer` now waits for the exact
  completion signal the issue #46 dispose-time guard already reads from the other end of the
  host lifecycle (the private `ServerMain.serverAssetsPacket` box: `packet` assigned by
  non-dedicated builds, Atlas's case, or `Length` bumped by dedicated ones), awaited on the
  game-thread tick scheduler so the pump keeps processing while the build finishes, and
  bounded at 1800 ticks (~60 s at the engine's nominal tick pace, mirroring the dispose-side
  bound) with an engine-drift `AtlasSetupException` on expiry. On the supported engines
  (verified by decompile on 1.20.12, 1.21.7 and 1.22.3) the build is queued at boot by
  `ServerMain.Launch()` and `HandleRequestJoin` itself blocks on the same signal, so a
  completed join normally settles the check instantly: the guard costs two cached-reflection
  reads per join and only actually waits when a mod kicked the player mid-join before the
  join's own wait ran (or on engine forks that defer the build to the first join, the field
  report's case). The probe reflection now has a single owner (`ServerAssetsBuildProbe`, its
  pure signal shape in `AssetsBuildSignal`) shared by both guards, and an engine whose signal
  layout drifted degrades exactly like the dispose side: skip the wait behind a one-time
  warning. The faulty enumeration itself is vanilla engine code (reported upstream at
  StratumServer/Stratum#151); Atlas closes the window scenarios could race it from.

## [0.9.0] - 2026-07-13

### Added

- Supported game-version floor lowered from 1.22.0 to 1.21.0, with 1.20.x compatible
  best-effort (issue #15, measured in docs/specs/2026-07-12-pre-122-compat.md). The entire
  compile-level gap below 1.22 is the server exit lifecycle, now owned by one runtime shim
  (`EngineCompat`): it installs the exit-state holder into whichever field the loaded engine
  has (`ServerMain.exitState` on 1.22+, `ServerMain.exit` before), adapts `Stop` between the
  1.22 `Stop(string, EnumExitMode, ...)` and the pre-1.22
  `Stop(string, string = null, EnumLogType = Notification)` shapes, and reads
  `GameVersion.NetworkVersion`/`ShortGameVersion` from the loaded assembly's metadata instead
  of the compile-time constants (which the C# compiler bakes into Atlas's IL, so a prebuilt
  Atlas on an older engine would otherwise have every test-player join kicked by the server's
  network-version check). The shim is boot-validated fail-fast: an engine whose layout
  drifted refuses to boot with an `AtlasSetupException` naming the game version and the
  missing symbol, and 1.19.x or older is rejected up front citing the floor (their boot API
  changes shape beyond what reflection can bridge). On 1.22.x the shim binds the modern
  members directly: zero behavior change, proven by the unchanged E2E suites. Also fixes the
  one measured pre-1.22 runtime difference: server-side entity positions are now read and
  written through `SidedPos`/`ServerPos` (pre-1.22 keeps `Pos` and `ServerPos` as two
  separate instances and only maintains `ServerPos` for headless joins; 1.22 unified them, so
  the fix is a no-op there) in `ITestPlayer.Position`, `TeleportTo`'s dimension check,
  `SpawnEntity` and the player-rollback position capture/restore. Honest matrix: 1.21.7 and
  1.20.12 verified live with the full engine E2E suite plus samples (rebuilt against each
  install's own dlls, the documented flow); 1.21.7 joins the per-push CI matrix, 1.20.12
  stays on the weekly sweep; the prebuilt-NuGet-binary-on-old-engine path is designed for but
  has no CI lane yet; 1.19.x and older stay unsupported.

- `[AtlasTheory]`: the theory-style counterpart to `[AtlasScenario]`. Combine with
  `[InlineData]`, `[MemberData]` or any other xUnit `DataAttribute` and each data row runs as
  its own scenario on the embedded server's game thread, with the row's values in its display
  name and rows passing/failing independently. The per-scenario settings (`FreshWorld`,
  `RollbackWorld`, `RestartWorld`, `StrictIsolation`, `TimeoutMs`) mirror `[AtlasScenario]`
  exactly and apply per row (each row is a full scenario of its own, with the same isolation
  mutual exclusions). All of xUnit's own theory behavior is inherited, not
  reimplemented: serializable rows are pre-enumerated at discovery time into one test case each
  (so they appear individually in VS Test Explorer), non-serializable data falls back to
  xUnit's standard runtime-enumerating test case, and a theory with no data fails with xUnit's
  own "No data found for ..." error. Rows run sequentially like every other scenario of a
  class, and `atlas run --parallel` keeps a theory's rows together with their class's worker.

### Changed

- Isolation summaries close the two observability gaps of the Manifold 3c validation
  (issue #71). The per-class summary is now emitted whenever the class ran ANY isolation mode:
  FreshWorld-only classes, previously silent, report their recycle count and measured cost
  ("2 FreshWorld recycle(s) (14.2 s total)"; the recycle is measured in the registry the same
  way restarts are). And the lazy first capture of a rollback class is its own line item
  instead of being folded into the rollback count, so N rollback scenarios no longer read as
  N-1 restores: the summary starts with e.g. "1 capture (1.2 s), 3 rollback(s) succeeded
  (0.4 s total)" and the arithmetic is self-explanatory; successful restores now carry their
  measured total too. Consumer-visible in the worker protocol: the `class-summary` event fires
  for FreshWorld-only classes as well and its `summary` string uses the new wording, but the
  event's fields and `v` (still 1) are unchanged, per the additive rules; the worker-protocol
  spec documents the widened emission rule and wording.

- Test players now reach `EnumClientState.Playing` (issue #74). Behavior change: `JoinPlayer`
  completes the engine's own join sequence by sending the real `ClientLoaded`/`PlayerReady`
  packets (26/29) after the inventory wait, so the SERVER runs its own transition instead of
  test players sitting one state short of visible. Joined players are now seen by everything
  that filters on `ConnectedClient.IsPlayingClient` or counts Playing players (Stratum's
  distance-based throttling, `GetPlayersAround`/`NearestPlayer`, playing-count broadcasts),
  and the engine's `PlayerNowPlaying` (and, on 1.22+, `PlayerReady`) events fire exactly as
  for a real client. Observable side effects of the higher fidelity: the join is announced in
  chat, the server streams world updates to the player's inert dummy buffers, natural entity
  spawning considers test players, and test players become valid interaction targets. The
  packets were originally skipped as out of scope, not because of a technical constraint; the
  decompiled 1.20.12/1.21.7/1.22.3 handlers confirmed none exists (every post-transition
  engine path is dummy-socket-safe or `IsSinglePlayerClient`-guarded), so Playing is the
  default with no opt-out. A player kicked by a mod DURING the join keeps today's behavior:
  `JoinPlayer` returns, the player never reaches `Playing`, and the kick is observed via
  `ITestPlayer.IsConnected`. A joined player that stays registered without reaching `Playing`
  now fails fast with an actionable `AtlasSetupException` (engine drift diagnosis).

### Fixed

- World rollback no longer races the engine's chunk thread on the shared savegame database
  connection. Playing test players (see above) keep chunk streaming active between scenarios,
  and the chunk thread's single-row reads (`ServerSystemSupplyChunks.TryLoadMapChunk` and
  friends) take no lock at all: the connection's `transactionLock` only serializes transaction
  blocks against each other, so a rollback transaction from the game thread made those reads
  throw mid-flight on 1.22.x ("Execute requires the command to have a transaction object when
  the connection ... is in a pending local transaction"), which the engine escalates to a full
  server shutdown; under CPU contention (4-core CI runners) this was deterministic. Capture
  reads and the restore's database phase now run inside the engine's own suspend window
  (`ServerMain.Suspend(true)`, the exact convention the engine's autosave uses for main-thread
  database access: it pauses every server thread and waits for each acknowledgment), with a
  guaranteed resume; a suspend that cannot be acquired in time degrades the rollback
  fail-closed to a full host recycle. `Suspend` is public and identical on 1.20.12, 1.21.7 and
  1.22.3, so the fix needs no new reflection and protects the pre-1.22 database layer too
  (same shared connection, different timing).

- The game-thread pump now notices an ENGINE-initiated shutdown and fails fast. When the
  engine stops itself (its reaction to an unhandled exception in one of its server threads:
  "Caught unhandled exception in thread '...'", stop reason "Exception during Process"),
  `ServerMain.Process()` becomes a silent sleep loop, and the pump previously spun on it
  forever: any engine crash became a job-timeout hang instead of a red test. The pump now
  watches the engine's public `stopped` flag (set first thing by every `Stop` since at least
  1.20.12), and a stop Atlas did not request is recorded as a host crash: pending tick waiters
  are faulted with the real cause, the scenario fails promptly with a `ServerCrashedException`
  whose message points at the server logs (the engine keeps the stop reason and the failing
  thread's stack only there), and teardown proceeds. Atlas's own stop paths are unaffected:
  they cancel the pump before ever calling the engine's `Stop`.

## [0.8.0] - 2026-07-11

### Added

- Isolation summaries and restart costs beyond stderr (issue #66, from the Manifold 0.7.0
  dogfooding). RestartWorld's cost is no longer invisible: the restart (shutdown + harvest +
  boot) is measured in the registry, attached to the requesting scenario's own test output
  ("[Atlas] world restarted: ... (cost 7.1 s, paid outside the scenario's reported
  duration).", the same channel degrade reports use, so it lands in the IDE test explorer,
  the TRX per-test output and `atlas run`), and accumulated into the per-class isolation
  summary ("2 restart(s) (14.1 s total)"); degraded rollbacks get the same treatment, their
  summary breakdown now carrying the total fallback-recycle cost. The per-class summaries
  themselves now travel beyond stderr: worker mode emits a new additive `class-summary`
  protocol event (documented in the worker-protocol spec; `v` stays 1) between the class's
  last test event and its class-end, fed by a reflection-installed harness sink plus a
  graceful final-host shutdown before the stream closes; `atlas run --parallel` prints each
  observed summary live, repeats them under an "Isolation summaries:" section in its final
  summary, and stores them as the aggregated TRX's run-level output
  (ResultSummary/Output/StdOut). Plain `dotnet test` and `atlas run` keep their stderr line
  unchanged.

- Mini-dimension support and mod-cooperation hooks for world rollback (issue #48, stage 3 of
  the snapshot/rollback design). Rollback now covers EVERY dimension: capture records loaded
  chunk columns as (X, Z, Dimension) triples (the database rows were dimension-keyed all
  along), marks loaded mini-dimension chunks dirty before the forced save so the engine's
  dimension-aware reload always finds complete columns, discards non-zero-dimension columns
  through the engine's own per-chunk unload helper (never persisting, and never firing
  column-unloaded events the engine itself does not emit for mini-dimensions), and reloads
  them via the public `LoadChunkColumnForDimension` with a dimension-aware completion check.
  Boot-time pregenerated mini-dimensions no longer disqualify rollback (the issue #48
  acceptance bar): the `MiniDimensionChunksLoaded` degrade reason is no longer produced (the
  enum member is kept, like `PlayersJoined` in stage 2, so recorded summaries and logs keep
  their meaning). New mod-cooperation contract for mods whose in-memory state is keyed to
  SaveGame data (registries, allocators, generated-marker stores): Atlas pushes two engine
  event-bus events synchronously on the game thread, `atlas:rollback:captured` (once per
  capture, after the snapshot is in memory) and `atlas:rollback:restored` (every restore,
  after the database and SaveGame globals are restored and BEFORE any chunk column reloads,
  so chunk-loaded handlers and ticks never observe desynced mod state), with versioned
  `TreeAttribute` payloads (`version` = 1, `generation`, `restoreCount`). The event name plus
  payload shape is the whole contract: cooperating mods reference only VintagestoryAPI and
  rebuild their state from `api.WorldManager.SaveGame` exactly as at boot; the listener is
  inert outside Atlas runs. A throwing handler degrades the rollback fail-closed under the
  new `ModHookFailed` reason ("mod rollback hook failed") and fails the scenario under
  `StrictIsolation`. Mods that cooperate through neither lifecycle events nor the hook remain
  a documented hard boundary (use `FreshWorld`); no unsound detection heuristics were added.
  Also ships the restore-cost instrumentation the spec asked for: every restore logs one
  stderr line with the measured duration and the dirty-columns-at-restore ratio (the numbers
  that would justify the deliberately deferred dirty-column filtering optimization).

- Player-aware world rollback (issue #47, stage 2 of the snapshot/rollback design):
  `[AtlasScenario(RollbackWorld = true)]` now works on classes with joined test players. The
  snapshot captures, per joined player, the playerdata blob the forced save wrote (restored
  verbatim into the database) plus the live state a reset needs; a rollback resets the live,
  still-connected player in place: position, watched attributes (health, saturation, custom mod
  trees, merged key-by-key so behaviors that cached sub-tree references keep working),
  inventories (the swapped-inventories duplicate-items case), world player data (game mode,
  move speed, picking range, spawn, hotbar slot, deaths) and per-player moddata. Players that
  joined AFTER the snapshot was captured are removed by the rollback, so the world returns
  exactly to its captured population; their engine-side identity caches and playerdata rows are
  purged and their joined-name claims released, so the same name can rejoin as a brand-new
  player (the rollback waits for the release, so an immediate rejoin never hits the
  duplicate-name guard). Restore ordering is player-safe by construction: post-capture players
  are removed while the world is still live, the database is restored before the in-memory
  unload (so any player-adjacent column the engine re-requests already reads snapshot bytes),
  and live players are reset in the same game-thread turn as the unload, before a single tick
  is pumped. The stage 1 guards are gone: capture no longer refuses joined players, and the
  players-joined setup error in the rollback path is removed (the `PlayersJoined` degrade
  reason is kept so already-recorded summaries and logs keep their meaning, but it is no longer
  produced); rollbacks on player-hosting classes count as plain successes in the per-class
  isolation summary. Not reset, documented boundary: a player's animation/interaction state
  (test players are headless) and the host-scoped privileges/role data, which is not world
  state. `RestartWorld` still rejects joined players: their connections die with the host.

## [0.7.0] - 2026-07-11

### Added

- `[AtlasScenario(RestartWorld = true)]`: restart-same-world isolation (issue #54), completing
  the isolation trilogy: `FreshWorld = true` recycles the host for a brand-new world (strongest
  isolation, one full boot); `RollbackWorld = true` restores the same host's world snapshot
  without a reboot (fastest, no restart); `RestartWorld = true` restarts the server for real
  and carries the world over, for scenarios asserting on what actually persists. Before the
  scenario runs, the class host is shut down gracefully (the engine's shutdown persists the
  world save), the save is harvested, and a replacement host boots against it in a fresh
  scratch directory (the harvested file is deleted once the replacement is up). The scenario
  then runs on a genuinely restarted server whose world survived a real save/load round trip,
  so persistence scenarios (SaveGame moddata, manifests, whatever a mod writes for reload) are
  finally writable. Costs one full boot, same as `FreshWorld`: the boot IS the round trip under
  test. Semantics: the three world flags are mutually exclusive (any combination is a setup
  error resolved before any boot); `StrictIsolation` with `RestartWorld` is a setup error too
  (a restart either works or fails the scenario hard, so there is no silent degrade to be
  strict about); a failed harvest fails the scenario with an `AtlasSetupException` and a
  replacement boot crash surfaces as-is, never a silent fallback; a class-level
  `[AtlasWorld(SaveFile = ...)]` composes naturally, the restart carrying forward the CURRENT
  world state (mutations included), not the original fixture; joined test players do not
  survive a restart (their connections die with the host), so requesting one with players
  joined fails the scenario instead of silently dropping them. The per-class isolation summary
  line now also reports `N restart(s)`.

- `atlas --version` (or `atlas version`): prints the package version (the informational version
  without the `+sha` build metadata) and exits 0; needs no scenario assembly and no VINTAGE_STORY.

- Rollback degrades are now visible in the standard workflow (issue #53). When a
  `[AtlasScenario(RollbackWorld = true)]` request cannot be honored and falls back to a full
  host recycle, the degrade is attached to the scenario's own test output, with the classified
  reason (players joined, mini-dimension chunks loaded, engine drift, or a generic
  capture/restore failure), the one-line failure detail and the measured cost of the fallback
  recycle. That output travels inside the test result, so it shows up in the IDE test explorer,
  in the TRX report's per-test StdOut and under `atlas run` (which now prints non-empty test
  output indented beneath the PASS/FAIL line); the one-line stderr warning remains. Previously
  the stderr line was the only signal, invisible at normal `dotnet test` verbosity, so a suite
  could silently pay full recycles everywhere (e.g. a fixture pregenerating mini-dimensions at
  boot poisoning every rollback) while the author believed rollback was active.

- `[AtlasScenario(RollbackWorld = true, StrictIsolation = true)]`: opt-in strict mode for
  suites that treat the rollback speedup as a contract. A degraded rollback FAILS the scenario
  with an `AtlasIsolationException` carrying the degrade reason instead of silently recycling.
  The host is still recycled before the failure surfaces, so later scenarios of the class keep
  running on a clean world: strictness changes visibility, not safety. A genuine server crash
  during the rollback attempt is never re-labelled and keeps surfacing as
  `ServerCrashedException`. Setting `StrictIsolation` without `RollbackWorld` is a setup error
  (only a rollback request can degrade, so there is nothing to be strict about).

- Per-class isolation summary: when a scenario class hands its host off (to the next class, the
  fixture harvest or process exit), Atlas prints one stderr line with the class's isolation
  outcomes, e.g. `[Atlas] isolation summary for MyMod.Tests.MyScenarios: 2 rollback(s)
  succeeded, 1 degraded to a full host recycle (mini-dimension chunks loaded x1), 0 FreshWorld
  recycle(s).` Classes that never requested rollback isolation stay silent. This is the honest
  place to see the isolation cost, which per-test durations hide (the restore or recycle happens
  outside the timed test body).

- `atlas fixture <Scenarios.dll> --scenario <substring> --out <fixture.vcdbs> [--force]`: builds
  the prebuilt world save that `[AtlasWorld(SaveFile = "fixtures/myworld.vcdbs")]` boots against,
  turning what used to be folklore (run a builder scenario, then harvest the save its graceful
  teardown wrote from the host's scratch data path) into a first-class command. The builder is an
  ordinary `[AtlasScenario]` whose side effect is building the world (place blocks, run commands,
  seed data): `--scenario` must select exactly ONE scenario by display-name substring (zero or
  several matches is a usage error listing the candidates, exit 2), the run uses the same
  in-process mechanics as `atlas run --filter`, and after the scenario passes and the host tears
  down gracefully the persisted save is copied to `--out` (parent directories created; an
  existing file is only overwritten with `--force`). A failing builder writes nothing and exits 1,
  so a broken builder can never silently produce a half-built fixture.

- `IWorldSession.PlaceSchematic(path, origin)`, plus an `EnumReplaceMode` overload: loads a
  block schematic (`.json`, e.g. a worldedit export) and places it with its minimum X/Y/Z
  corner at the given position, returning the placed block count. This makes "how do I load a
  prebuilt structure in a test" first-class next to the world-fixtures story:
  `[AtlasWorld(SaveFile = ...)]` loads a whole prebuilt world, `PlaceSchematic` stamps a single
  prebuilt structure into the running one. Paths resolve like every other fixture path
  (absolute, or relative to the test assembly's directory), a missing or malformed file fails
  with `AtlasSetupException` carrying the resolved path and the engine's error, and placement
  mirrors the engine's worldedit import: blocks, decors, block entities (with their saved
  data) and any entities stored in the schematic.

- Fail-fast preflight for a stale test-output `VintagestoryAPI.dll` copy (issue #49, option 1):
  before the embedded server boots, Atlas compares the test output's copy byte-for-byte
  (size + SHA-256) against the `VINTAGE_STORY` install's copy and fails with a clear
  `AtlasSetupException` naming both files (size, short hash, assembly version) and both remedies
  (rebuild against the target install, or copy the install's dll AND pdb over the local ones).
  This is the multi-install trap of differential runs: repointing `VINTAGE_STORY` at a different
  install without rebuilding loads the target's `VintagestoryLib` against the stale local
  `VintagestoryAPI` and dies deep into boot with a cryptic `MissingFieldException`. A version
  check would not catch it (forks rebuild the API at the same assembly version), so the
  comparison is content-based. Consumers that do not copy the dll (`Private=false`) are
  unaffected: the check skips silently when no local copy exists.

### Fixed

- In-process `AssemblyRunner` disposal race (issue #59): everywhere Atlas drives xunit's
  `AssemblyRunner` in-process (`atlas run`, the worker mode and the engine's own nested-runner
  E2E test), disposal now waits (bounded, 30 s) for the runner to report `Idle` plus a short
  grace, and LEAKS the runner instead of disposing it if it never idles. xunit.runner.utility
  2.x disposes the runner's completion events while its worker thread can still be heading into
  its final `WaitOne()` (AssemblyRunner.cs:263); the worker then dies with an unhandled
  `ObjectDisposedException` on a pool thread, which kills the whole process. That is how one
  flaky nested run sank an entire green CI leg: a leaked event in a finishing process is
  harmless, a disposed-while-awaited event is a process kill.

## [0.6.0] - 2026-07-07

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

- `atlas run --parallel [N]`: multi-process orchestration over the assembly's scenario classes,
  stage 2 of the parallelization design (issue #1). The orchestrator discovers the classes
  without booting anything, then drains a greedy per-class queue with N worker subprocesses
  (each one `atlas run <dll> --worker --classes <class>`: one live server per worker, one class
  per dispatch, workers pull the next class as they free up). N defaults to
  min(max(1, cores / 2), class count). Results stream back over the stage 1 JSONL protocol and
  print live per test; the final summary adds per-class wall clocks and the speedup versus the
  sum of class times (what running them back to back would have cost). A worker that dies
  without a well-formed `run-end`, exits nonzero without a failing scenario, or outlives
  `--worker-timeout <seconds>` (default 600 per class; the whole worker process tree is killed)
  is translated into a synthesized failed class carrying a stderr tail for forensics, and the
  queue keeps draining: a crashed worker can fail its class, never shorten the test list.
  `--trx <path>` writes one aggregated VSTest-style TRX report covering every class, so CI
  artifact upload and TRX tooling keep working without `dotnet test`. `--parallel` is
  incompatible with `--worker` and `--list`; `--worker-timeout` and `--trx` require it.

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
