# Parallel scenario execution: multi-process orchestration

Date: 2026-07-06
Status: draft (design for issue #1; no implementation yet)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[feasibility spike](../feasibility-spike.md)
Related: issue #3 (`atlas run` CLI facade, designed in parallel; this document assumes its
single-process filtered runner exists), issue #8 (shutdown NRE caused by cross-lifecycle
static poisoning)

## Problem

An Atlas suite is strictly sequential today: one live server per process, scenario classes
run one after another (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`,
enforced by `HostRegistry`). Each class pays a server boot (~4 s for a superflat world on the
spike machine) plus its scenario time, plus one extra boot per `FreshWorld = true` scenario.
Wall clock therefore grows linearly with the number of classes, and a single scenario that
runs to its 60 s watchdog stalls the whole suite for that long. As consumers (Manifold,
Chart) accumulate scenario classes, the suite time budget will be blown by design, not by
accident.

The parallel unit is the scenario CLASS: it is already the fixture boundary (one server +
world per class), classes are independent by construction (cross-class isolation is total),
and scenarios within a class intentionally share world state, so they must stay sequential.

The constraint that shapes everything: **at most one live server per process**. Parallelism
must come from multiple processes, and the question is who spawns them, how work is
partitioned, and how results flow back into a single `dotnet test`-grade report.

## Constraint evidence: the engine statics

Decompiled from Vintage Story 1.22.2 (`VintagestoryLib.dll`, `VintagestoryAPI.dll`) for this
design; the feasibility spike found the first four, this pass completes the list.

| Static | Declared in | Why a second live server breaks |
|---|---|---|
| `ServerMain.Logger` | `Vintagestory.Server.ServerMain` | Process-wide logger. `ServerMain.Dispose()` runs `Logger?.Dispose(); Logger = null;`: one server's teardown nulls the logger under the other (the issue #8 NRE is exactly this, surfacing through `ServerSystemMonitor.Dispose()`). |
| `TyronThreadPool.Inst` | `Vintagestory.API.Common.TyronThreadPool` | Single global pool (`public static TyronThreadPool Inst = new TyronThreadPool();`). `ServerMain.Dispose()` calls `TyronThreadPool.Inst.Dispose()`: one server disposing kills the other's worker threads. |
| `ServerMain.ClassRegistry` | `Vintagestory.Server.ServerMain` | `public static ClassRegistry`; nulled in `Dispose()`, repopulated per boot. |
| `GamePaths.DataPath`, `GamePaths.CustomLogPath` | `Vintagestory.API.Config.GamePaths` | Every save, log, config and cache path derives from one static `DataPath`. Two servers would write into each other's scratch worlds. `ServerHost.ConfigureEngineStatics` sets it per boot, which only works because boots never overlap. |
| `GamePaths.Binaries` | `Vintagestory.API.Config.GamePaths` | `=> AppDomain.CurrentDomain.BaseDirectory`, redirected once per process via the `APP_CONTEXT_BASE_DIRECTORY` slot (`GameEnvironment.Initialize`). Harmless for same-install servers, but confirms the engine reads process-level ambient state. |
| `RuntimeEnv.ServerMainThreadId` | `Vintagestory.API.Config.RuntimeEnv` | Single `static int`, assigned inside `Launch()` (`RuntimeEnv.ServerMainThreadId = Environment.CurrentManagedThreadId;`). Two pumps would fight over "am I the game thread" checks: silent misbehavior, not just a crash. |
| `ServerMain.FrameProfiler` + `FrameProfilerUtil.offThreadProfiles` | `ServerMain` / `Vintagestory.API.Common.FrameProfilerUtil` | Static profiler and a static `ConcurrentQueue<string>` shared by all off-thread profiling. |
| `Lang` tables | `Vintagestory.API.Config.Lang` | `static Dictionary<string, ITranslationService> AvailableLanguages`; `Lang.PreLoad` per boot mutates it globally. |
| `WildcardUtil.RegexCache` | `Vintagestory.API.Util.WildcardUtil` | The `ServerMain` constructor calls `WildcardUtil.ClearRegexCache()`: a booting server wipes a running server's cache mid-flight. |
| Process current directory | set by `GameEnvironment.Initialize` | The mod loader's Mono.Cecil scan resolves `VintagestoryAPI` against the CWD (issue #32). Same value for same-install servers, but again: the engine assumes it owns the process. |

Sequential reuse of these statics in one process is proven safe (spike ran two full
lifecycles; the constructor re-initializes the registries it depends on). Concurrent use is
unsafe in several independent ways. This is not fixable from our side without patching the
engine.

## Rejected: N servers in one process via AssemblyLoadContext

Statics live per (assembly, ALC) pair, so loading `VintagestoryLib` + `VintagestoryAPI` into
one custom ALC per server would, in principle, give each server its own `Logger`,
`TyronThreadPool.Inst`, `GamePaths`, and so on. Investigated and rejected on three grounds:

1. **Mod loading escapes the ALC.** `Vintagestory.Common.ModAssemblyLoader.LoadFrom` is
   `Assembly.UnsafeLoadFrom(path)` (decompiled), and on modern .NET `LoadFrom`/
   `UnsafeLoadFrom` always load into the DEFAULT ALC. So the mod-under-test and
   `AtlasBridge.dll` would land in the default ALC, and their `VintagestoryAPI` references
   would resolve there too (our `AppDomain.AssemblyResolve` hook in `GameEnvironment` also
   uses `Assembly.LoadFrom`). Result: type identity splits. The engine copy inside ALC X
   scans mod assemblies for ITS `ModSystem` type; the mod derives from the default ALC's
   `ModSystem`. Zero systems load, or `InvalidCastException` at the first boundary crossing.
   Working around this means Harmony-patching the engine's assembly loading: fighting the
   engine on its own turf.
2. **Author code shares the identity problem.** Scenario assemblies compile against
   `VintagestoryAPI`; the `ICoreServerAPI` instance handed to a scenario must be the same
   runtime type the scenario was compiled against. xUnit v2 cannot place a test class into a
   chosen ALC, so the entire stack (adapter, scheduler, bridge, author assembly) would have
   to be loaded per ALC and driven through reflection-only seams. That is a hand-built,
   worse process boundary.
3. **Process-level state stays shared regardless of ALC.** The `APP_CONTEXT_BASE_DIRECTORY`
   slot, the current directory, environment variables, console streams, `AppDomain` event
   hooks, and native libraries (`libe_sqlite3.so`, `libzstd.so` under `Lib/`: a native
   library loads once per process; N managed engine copies hammering shared native state
   concurrently is unexplored territory). ALC isolates managed statics only.

There is also no resource win to chase: assets and world state are per `ServerMain` instance,
so memory cost is per server either way, and each server spawns its ~10 engine threads
regardless.

Verdict: the honest isolation boundary is a child process. The OS provides cleanup, crash
containment, and debuggability for free. Supporting evidence that multi-process is the
engine-sanctioned shape: running several dedicated VS server processes on one machine is a
normal community deployment, and Atlas servers (`isDedicatedServer: false`) open no sockets
at all, so parallel processes cannot even conflict on ports. Scratch data paths are already
unique per host (`%TMP%/atlas/<guid>`), and the install directory is only ever read.

## What a multi-process orchestrator must do

1. **Discovery**: enumerate scenario classes (and their scenario counts) without booting a
   server.
2. **Partitioning**: class = unit of dispatch. Never split a class across processes
   (world-sharing semantics), never run two classes in one process concurrently
   (`HostRegistry` will correctly throw).
3. **Workers**: N child processes, each running its assigned classes sequentially with the
   existing engine, unchanged. `VINTAGE_STORY` is inherited; no other environment is needed.
4. **Aggregation**: merge per-worker results into one report with unchanged fully qualified
   test names, a correct process exit code, and TRX output so `ci.yml`'s
   `--logger trx` + artifact-upload contract keeps working.
5. **Failure translation**: a worker that dies or wedges must surface as failed results for
   its in-flight class (with the scratch data path for log forensics), never as a silently
   shorter test list.

Facts already on the shelf:

- xUnit v2's own parallelism is thread-based within one process (collections). There is no
  per-class process mode. Atlas keeps `DisableTestParallelization` for the in-process run
  path no matter what.
- VSTest runs each test CONTAINER (dll) in its own testhost process, and
  `dotnet vstest /Parallel` runs containers concurrently. Assembly-level process parallelism
  exists today for free; class-level does not.
- `xunit.runner.utility` (already a dependency of `Atlas.Engine.Tests`) gives a worker
  everything it needs: `AssemblyRunner.WithoutAppDomain(dll)` plus
  `AssemblyRunnerStartOptions.TypesToRun` (an array of class FQNs) plus `TestCaseFilter`.
  The pattern is proven in-repo: `NestedRunnerTests` drives the GuineaPig assembly exactly
  this way inside `dotnet test`.
- TRX is plain XML. VSTest only emits it for tests it ran itself, so a standalone
  orchestrator writes its own (a bounded, few-hundred-line serializer, testable in
  `Atlas.Pure.Tests`).

## Options

### Option 0: assembly-level splitting (zero code, stopgap)

Split scenario classes across several test projects; run containers in parallel
(`dotnet vstest /Parallel`, or simply overlap `dotnet test` invocations in CI: the two
existing E2E containers, `Atlas.Engine.Tests` and `Sample.Scenarios`, already run
sequentially in each matrix leg and could overlap today).

- Cost: none (documentation, one CI edit).
- Value: immediate, and it validates the "N server processes on one runner" resource math
  before any real code is written.
- Limits: the parallel grain is the author's project layout, which is a terrible API
  ("create another csproj to go faster"). A baseline, not a solution.

### Option A: orchestrator inside a VSTest adapter

Ship a custom `ITestExecutor` that, instead of running scenarios in-process, spawns
per-class child processes and reports results through the recorder. `dotnet test` UX is
preserved perfectly, TRX comes free.

- Cost: high. The VSTest adapter surface is notoriously fiddly: discovery sandboxing,
  testhost lifecycle, cancellation plumbing, `--filter` translation, and a recursion hazard
  (the child must run scenarios WITHOUT re-entering the parallel adapter). Debugging a
  process tree rooted inside a testhost is painful.
- Strategic cost: Microsoft is actively migrating the ecosystem from VSTest to
  Microsoft.Testing.Platform (MTP). A bespoke VSTest adapter is an investment in the
  platform being sunset.

### Option B: orchestrator in the `atlas run` CLI (recommended)

`atlas run --parallel N` on top of issue #3's CLI. The CLI already needs "run a filtered set
of scenario classes in one process" as its core primitive; the orchestrator is a scheduling
and reporting layer over exactly that primitive, invoked as child processes:

```
atlas run tests/Foo.Scenarios/bin/... --parallel 4
  ├── worker: atlas run <dll> --classes ClassA --report jsonl   (one live server)
  ├── worker: atlas run <dll> --classes ClassB --report jsonl   (one live server)
  └── ... work-stealing queue, one class per dispatch
```

- Composes with issue #3 instead of competing with it: worker mode IS the CLI's
  single-process run with a class filter and a machine-readable reporter.
- The sequential `dotnet test` path (xUnit adapter) stays untouched: IDE runs, debugging,
  and the inner loop keep working exactly as today; parallelism is opt-in where wall clock
  matters (CI, full local suite).
- Not blocked on issue #3 shipping: an interim worker is ~50 lines over
  `xunit.runner.utility` (`AssemblyRunner` + `TypesToRun`), the pattern
  `NestedRunnerTests` already proves. The orchestrator/worker protocol is the stable part;
  the worker's internals can swap from runner.utility to the issue #3 engine-native runner
  later without touching the orchestrator.
- Cost: medium. JSONL event protocol, process pool with per-worker outer timeout, crash
  translation, TRX writer, console progress. All of it is plain .NET, unit-testable without
  VS, and debuggable with standard tools.
- Trade-off accepted: `dotnet test` itself does not get faster; CI and power users switch
  the E2E invocation to `atlas run`. A thin `dotnet test` bridge can be layered later
  (Stage 3) if demand appears.

### Option C: migrate to xUnit v3

xUnit v3 test projects are standalone executables; the runner talks to them out-of-process
by design, the exe takes `-class` filters, and MTP integration provides a TRX extension. On
paper this smells like the answer; honestly assessed, it is not:

- v3's in-assembly parallelism is STILL thread-based (collections on threads, same process).
  Per-class process fan-out remains custom orchestration work in v3, exactly as in v2. What
  v3 buys is a cheaper worker (the test exe is already a filterable process): the smallest
  piece of Option B.
- Migration cost is the largest of any option: the whole `Atlas.XUnit` extensibility layer
  (`AtlasScenarioDiscoverer`, `AtlasTestCase` with its custom serialization,
  `AtlasTestCaseRunner`/`AtlasTestInvoker`, the watchdog integration) sits on v2 APIs that
  v3 rewrote; `Xunit.Runners.AssemblyRunner` (NestedRunnerTests) changed as well. Every
  failure-shape guarantee in the GuineaPig E2E suite would need revalidation.
- Consumers must migrate their test projects to v3 at the same time (v2 and v3 do not mix
  in one project).

Verdict: v3 is a WHEN-question driven by ecosystem pressure (v2 bitrot, MTP becoming the
default), not a HOW-answer for parallelism. Revisit after Option B ships; the orchestrator
survives a v3 migration intact because it only depends on the worker contract.

### Summary

| | Code cost | `dotnet test` stays fast path | Risk | Delivers class-level parallelism |
|---|---|---|---|---|
| 0: assembly split | none | yes (containers) | none | no (project-level only) |
| A: VSTest adapter | high | yes | high (VSTest quirks, MTP sunset) | yes |
| B: `atlas run --parallel` | medium | sequential only | low | yes |
| C: xUnit v3 | highest | yes (still sequential) | medium | no (still needs an orchestrator) |

## Recommendation: Option B, staged

### Stage 0 (now, CI-only): overlap containers, measure

Run the two E2E containers concurrently within each matrix leg (background one `dotnet test`,
wait, or `dotnet vstest /Parallel`). Record wall-clock and memory numbers per leg: this is
the data that sets the default `--parallel` for Stage 2. Document assembly splitting as the
interim recipe for consumers with painful suites.

### Stage 1: worker mode and the result protocol

- `atlas run <test-assembly> --classes A,B --report jsonl [--report-to <file|fd>]`:
  runs the listed classes sequentially in this process (issue #3's primitive; interim
  implementation via `xunit.runner.utility` if the CLI's native runner is not ready).
- JSONL event contract (the stable seam of the whole design): `run-start`,
  `class-start`, `scenario-start`, `scenario-result` (pass/fail/skip, duration, failure
  message + stack, scratch data path), `class-end`, `run-end`. Exit codes: 0 all passed,
  1 scenario failures, 2+ infrastructure failure (boot failed, bad arguments).
- Workers keep the existing in-process watchdog and dead-host fail-fast semantics
  unchanged; `HostRegistry`'s exclusivity guard keeps guarding the in-process invariant.

### Stage 2: the orchestrator

- `atlas run <test-assembly> --parallel N`: discover classes, greedy work queue (one class
  per dispatch, workers pull the next class when done), default
  `N = min(Environment.ProcessorCount / 2, classCount)`, always `1` unless `--parallel` is
  given (no behavior change by default).
- Per-worker outer timeout (sum of assigned scenario watchdogs plus boot allowance):
  defense in depth above the in-process watchdog. A killed or crashed worker's in-flight
  class is reported failed with the worker's exit code, captured stderr (ServerHost writes
  its diagnostics there), and the scratch data path.
- Reporting: live console progress, final summary, and a TRX file whose test FQNs match
  what `dotnet test` produced historically (tooling continuity). CI keeps its
  upload-artifact steps unchanged apart from the path.
- CI: each of the 4 matrix legs switches its two E2E steps to `atlas run --parallel 2`
  (ubuntu-latest: 4 vCPU, 16 GB; revise with Stage 0 numbers). The `VINTAGE_STORY` export
  and the server-download cache steps are untouched: workers inherit the variable.

Expected effect: suite wall clock drops from the SUM over classes to roughly the MAX over
workers; boot overhead (~4 s per class, more with `FreshWorld`) and watchdog-bound worst
cases amortize across workers. Side benefit: one process per class makes the issue #8
hazard structurally impossible across classes: an abandoned game thread's late
`ServerMain.Dispose()` can no longer null statics under the NEXT class's host, because the
next class lives in a different process.

### Stage 3 (optional, demand-driven)

- Duration cache (persisted per assembly) for longest-first scheduling.
- A thin `dotnet test` bridge (MTP-first, given VSTest's trajectory) that shells out to the
  orchestrator, if consumers insist on a single entry point.
- Cross-runner sharding in CI (a `shard` matrix dimension calling
  `atlas run --shard i/n`) if a single runner saturates before the suite is fast enough.

## Open questions

- Memory per embedded superflat server under load: measure in Stage 0; it, not CPU, likely
  caps `N` on 16 GB CI runners.
- Where the Stage 3 duration cache lives (TestResults artifact, committed json, or local
  cache dir).
- Issue #3's discovery mechanism (reflection over `[AtlasScenario]` vs
  `xunit.runner.utility` discovery): this design only needs "list classes" and "filter by
  class", both satisfy it; align when that spec lands.
- Windows behavior under concurrent asset reads from one install (expected fine: multiple
  dedicated servers per machine is a supported deployment and assets are opened read-only;
  verify the FileShare flags before declaring victory).
- Should `FreshWorld = true` scenarios weigh their class heavier in scheduling (each one is
  an extra boot)?
- Whether worker stdout/stderr beyond the JSONL channel should be attached per-scenario in
  TRX (probably yes for the crash path, noise for the pass path).
- MTP/xUnit v3 reassessment trigger: define the concrete signal (e.g. xUnit v2 dropping
  support for a .NET version Atlas needs) rather than revisiting ad hoc.
