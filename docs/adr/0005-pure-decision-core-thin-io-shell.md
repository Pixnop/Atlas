# 0005. Pure decision core with a thin IO shell

Status: accepted.

## Context

Every interesting decision in Atlas sits next to something expensive or unavailable: the
disk, the clock, the loaded engine assemblies, a booted server. Testing those decisions
through the thing they touch means a full boot, a real temp directory, or a real wait. A
boot costs seconds; the version matrix multiplies it by six.

Most of those decisions do not actually need the resource. Whether a dll should be staged is
a function of two version strings and a file's presence. Whether a scratch directory may be
deleted is a function of what the teardown observed. Whether a poll should give up is a
function of a count and a bound.

## Decision

Split each of them in two. A pure core takes the facts as arguments and returns a decision
or an outcome object, with no IO, no environment reads and no engine types it does not need.
A thin shell gathers the facts, calls the core, and performs the effect. Where the effect
is a loop or a probe, the shell's dependencies are injected as delegates, so bounds and
give-up paths are exercised without time or disk.

The pure suite is the primary net for these cores. It needs no install to run and finishes
in well under a second, which is what makes it usable in the inner loop.

## Consequences

- Roughly five pure tests for every engine test (663 against 123 as this record is written).
  The pure ones carry the branch coverage; the engine ones carry the proof that the contract
  holds against a real server.
- Error branches become cheap. A staging decision's failure modes are theory rows rather
  than fault injection against a live install.
- The split is a convention, not a type. Nothing enforces it, and a new decision written
  inside its shell simply becomes untestable without a boot, which is how the reflection
  lookups in `WorldSnapshot.Create` ended up where they are.
- Pairs cost a file each. This is only worth it where the decision has branches; a one-line
  rule stays where it is.

## Source files

Pure core, then its shell:

- `src/Atlas/Internal/Bootstrap/EngineStaging.cs:53` and `:193`, shell
  `src/Atlas/Internal/Bootstrap/EngineStager.cs:117`.
- `src/Atlas/Internal/Hosting/AssetsBuildSignal.cs:15`, shell `ServerAssetsBuildProbe.cs`.
- `src/Atlas/Internal/Hosting/SimulationTickSignal.cs`, shell
  `EntitySimulationTickCounter.cs`.
- `src/Atlas/Internal/Hosting/ScratchRetention.cs:16` and `ScratchCleanup.cs:65`, shell
  `HostRegistry.SweepScratch` at `src/Atlas.XUnit/Internal/HostRegistry.cs:384`.
- `src/Atlas.XUnit/Internal/WorldIsolationResolver.cs`, shell `AtlasTestInvoker.cs:89`.
- `src/Atlas.Cli/StagePathResolution.cs` and `StageReport.cs`, shell `StageRunner.cs`.
- `src/Atlas.Cli/WorkerRunSession.cs`, shell `WorkerRunner.cs`.
- `src/Atlas.Cli/TrxDiff.cs`, shells `DiffConsoleReport.cs` and `DiffJsonReport.cs`.

Delegate-injected shells: `src/Atlas.Cli/RunnerDisposal.cs:38`,
`src/Atlas/Internal/Hosting/ScratchCleanup.cs:65`, `src/Atlas.Cli/FixtureOutput.cs:13`,
`src/Atlas.Cli/ScenarioAssemblyResolver.cs:34`.
