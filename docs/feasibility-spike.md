# Feasibility spike: in-process headless Vintage Story server for integration testing

Date: 2026-07-02
Game version probed: Vintage Story 1.22.2 (net10.0), install at `%VINTAGE_STORY%`
Method: decompilation of `VintagestoryServer.dll` / `VintagestoryLib.dll` (ILSpy), plus an
empirical smoke experiment (probe mod + console host, run twice in one process).

## Verdict: GO

Every spike question has a positive, empirically verified answer. The in-process approach
is retained; no fallback (child process over IPC) is needed.

| Question | Answer |
|---|---|
| Headless in-process bootstrap | Yes, via the same path the singleplayer client uses |
| Load arbitrary mod + test mod | Yes, `--addModPath` / `ServerConfig.ModPaths` (folder, zip, dll, raw .cs) |
| Determinism | Yes at observation level; two fresh worlds with the same seed produced bit-identical probe results |
| Isolation between scenarios | Sequential same-process restart works (proven twice in one process) |
| Existing precedent | VinTest only (client-driven, GUI process); the headless niche is empty |

## 1. Bootstrap: the engine supports embedding by design

`VintagestoryServer.exe` is a thin shim over `Vintagestory.Server.ServerProgram.Main`,
which boils down to:

```csharp
var server = new ServerMain(startServerArgs, rawArgs, progArgs, isDedicatedServer);
server.exitState = new GameExitState();
server.PreLaunch();
server.Launch();                    // blocks until world ready, ~4 s on a superflat world
do { server.Process(); }            // ONE pump iteration of the main game loop
while (!server.exitState.Exit);
server.Stop(reason, exitMode);
server.Dispose();
```

The singleplayer client (`ClientProgram.ServerThreadStart`) hosts a `ServerMain` inside its
own process with `isDedicatedServer: false` and in-memory dummy sockets. Embedding a server
in a foreign process is therefore a first-class engine scenario, not a hack.

Key facts:

- **The thread that calls `Launch()` becomes the game thread** (`RuntimeEnv.ServerMainThreadId`
  is set to the current thread inside `Launch`). The test runner can therefore *be* the game
  thread: call `Process()` in a loop and run assertions between iterations with full,
  race-free access to the server API. Marshalling via `EnqueueMainThreadTask` is only needed
  for code running off-thread.
- With `isDedicatedServer: false`, no console thread and no real TCP/UDP sockets are created
  (socket slots stay null; every network call is null-safe). No port is opened.
- `StartServerArgs` gives full programmatic world control: `Seed` (string, parsed or hashed),
  `WorldName`, `SaveFileLocation`, `PlayStyle`, `WorldType`, `WorldConfiguration` (JSON),
  `MapSizeY`, `DisabledMods`.

### Bootstrap gotchas (all solved in the smoke experiment)

1. **`GamePaths.Binaries` is `AppDomain.CurrentDomain.BaseDirectory`.** When the host is not
   the game exe, asset and library probing breaks (`ModCompilationContext` hard-requires 17
   reference dlls at startup, even if no `.cs` mod is compiled). Fix, before touching any
   game type:
   `AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", installDir + Path.DirectorySeparatorChar)`.
   This redirects `BaseDirectory`, so `GamePaths.Binaries`, `GamePaths.AssetsPath` and the
   mod compiler all resolve against the install.
2. **Assembly resolution**: replicate `AssemblyResolver` (hook `AppDomain.AssemblyResolve`,
   probe `install`, `install/Lib`, `install/Mods`).
3. **`GamePaths.DataPath`** must point at a scratch folder per scenario (config, logs, saves
   land there). A default `serverconfig.json` is generated on first launch.
4. **`StartServerArgs.WorldConfiguration` must be non-null** (empty `{}` JsonObject is fine),
   otherwise `SaveGame.SetNewWorldConfig` NREs.
5. **`ServerMain.api` is `internal`.** The host cannot touch `ICoreServerAPI` directly;
   world access and assertions belong in a harness mod (`ModSystem.StartServerSide`), which
   is the natural Atlas architecture anyway. (Alternative if ever needed: the game loads mod
   dlls into the default AssemblyLoadContext via `Assembly.UnsafeLoadFrom`, so a dll loaded
   by both host and ModLoader from the same path shares statics; the spike used file-based
   signaling instead.)
6. Vanilla content mods (`game`, `creative`, `survival`) live in `install/Mods` and are found
   through the default `ModPaths` entry `"Mods"`, resolved relative to the **current working
   directory** - either run with CWD = install dir or set `ModPaths` explicitly.

## 2. Mod loading

`ServerSystemModHandler.OnLoadAssets` builds the search list from `ServerConfig.ModPaths`
plus `--addModPath` (`ServerProgramArgs.AddModPath`, repeatable). Accepted forms per entry
found in a search folder: **directory, `.zip`, `.dll`, `.cs`** (compiled on the fly with
Roslyn). Dll mods declare identity via `[assembly: ModInfo(...)]`.

The smoke experiment dropped a probe mod dll into a staging folder passed via `AddModPath`;
it was discovered, dependency-sorted and started alongside the vanilla mods:
`Mods, sorted by dependency: atlasprobe, game, creative, survival`.

So: the mod-under-test (any form) + the Atlas harness mod (dll) go into one staging folder;
no game files are touched.

## 3. Determinism

- **Tick pump ownership**: the host drives `Process()`; each call runs every `ServerSystem`
  whose wall-clock interval elapsed, fires tick listeners, then executes queued main-thread
  tasks. With a 1 ms listener interval, 20 `Process()` calls delivered exactly 20 ticks.
- **Caveat**: system scheduling inside `Process()` is *wall-clock based*
  (`totalUnpausedTime.ElapsedMilliseconds` against `Config.TickTime`, default 33.33 ms, and
  per-system update intervals). Atlas gets reproducible *ordering* (single game thread) and
  reproducible *world state*, but not bit-exact per-tick timing. A "wait N ticks" primitive
  observed via a tick listener is reliable; "exactly N engine ticks of subsystem X" is not
  guaranteed without patching the clock (Harmony is available if ever needed - out of scope).
- **`Config.TickTime` is configurable**: lowering it makes tests run faster than real time.
  `ServerMain.Suspend(bool)` can freeze ticking entirely.
- **Fixed seed works**: seed `"424242"` produced spawn `(512000, 512000)`, terrain height 2,
  `game:soil-medium-normal` ground block, calendar at 872 h - **bit-identical across two
  independent world creations**.
- **Superflat worlds are cheap**: `PlayStyle = "creativebuilding"` + `WorldType = "superflat"`
  boots and generates spawn chunks in ~4 s total per server on this machine.
- Worldgen randomness uses `LCGRandom` seeded by position + world seed (see vault note
  `60-Engine/Threading-modele.md`), which is what makes same-seed worlds reproducible.

## 4. Lifecycle and isolation

Empirically proven: **two full server lifecycles (create world, launch, tick, stop, dispose)
in the same process**, second run unaffected, results identical. Shutdown is clean:
`Stop()` suspends ticking, runs the shutdown phase, saves the world, joins all 10 server
threads ("All threads gracefully shut down"), then `Dispose()` releases the rest.

The engine is designed for this: the singleplayer client starts/stops embedded servers every
time a world is opened, and the `ServerMain` constructor re-initializes the static registries
it depends on (`WildcardUtil.ClearRegexCache`, tag registries, converters).

Watchpoints for Atlas (sequential reuse is safe; these forbid *parallel* servers in one
process):

- `ServerMain.Logger`, `GamePaths.DataPath`, `RuntimeEnv.ServerMainThreadId` are global.
- `ServerMain.Dispose()` disposes the global `TyronThreadPool.Inst` (harmless for the next
  run in practice - proven - but shared).
- Mod assemblies are never unloaded (default ALC); reusing the same mod dll across scenarios
  is fine, hot-swapping a rebuilt dll within one process is not.

Recommendation: **one process can host many sequential scenarios** (fast path), with
"fresh world per scenario" as the default isolation unit. A process-per-scenario mode can
be added later purely as an option, not as a necessity.

## 5. Prior art

- **VinTest** (Artalus, May 2026, MIT - https://github.com/Artalus/vintest) is the only
  existing VS test framework. It drives the **full graphical client** (`VintageStory.exe`)
  via a Cake build task, polls `TestResults/results.json`, and ships a VS Code extension.
  Very young (1 release, 37 commits), not on the Mod DB.
  Reusable ideas: `[GameTest]` attribute discovery, yield-based test steps (suited to
  tick-based games), results-file contract.
- Nothing official from Anego Studios (no GameTest equivalent in `vsapi`/`vsessentialsmod`).
- The headless-server/CI niche Atlas targets is **empty**.

## 6. Retained approach

1. Atlas hosts an embedded `ServerMain` (`isDedicatedServer: false`), one scenario at a time
   per process, fresh world per scenario, fixed seed, superflat default.
2. The Atlas runner thread calls `Launch()` and owns the `Process()` pump, making it the game
   thread; scenario steps and assertions execute between pump iterations.
3. World/API access from scenarios goes through the Atlas harness mod (`ModSystem` receiving
   `ICoreServerAPI`), injected via a staging mods folder next to the mod-under-test.
4. Reporting via a results-file contract (console + TRX/JUnit later, per Phase 1 decisions).

Smoke experiment sources (probe mod + host, ~150 lines total) are throwaway spike code; the
Phase 2 smoke milestone will rebuild them properly inside the Atlas architecture.

## 7. Out of scope, confirmed unknowns

- Testing against a *real* mod-under-test with heavy worldgen (Manifold) - Phase 2 smoke.
- Linux/CI headless run (nothing in the bootstrap is Windows-specific; the dedicated server
  officially runs headless on Linux, but CI wiring is future work).
- Parallel scenarios in one process - forbidden by engine statics; not needed for v1.
- API note: the game reports `api v1.21.0` even on game 1.22.2; the NuGet `VSApi` versioning
  to target will be settled when the csproj is created.
