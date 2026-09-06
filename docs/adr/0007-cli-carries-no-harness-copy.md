# 0007. The CLI carries no harness copy and reaches the harness by name

Status: under review.

## Context

`atlas` ships as its own dotnet tool, `Pixnop.Atlas.Cli`, on its own release cadence. The
scenario assembly it runs ships the harness: `Atlas`, `Atlas.XUnit` and `AtlasBridge` are in
that assembly's own output directory, at whatever version the test project referenced.

If the tool carried its own copy of those assemblies, the copy would win at load time and
the scenarios would run against a harness the author never chose: version skew, silently.

Two of the CLI's operations still need something from inside the harness. `atlas fixture`
has to shut the builder scenario's host down gracefully and learn where the save landed.
Worker mode has to install a sink so per-class isolation summaries reach the JSONL stream
instead of only stderr.

## Decision

The CLI resolves `Atlas`, `Atlas.XUnit` and `AtlasBridge` out of the scenario assembly's own
directory at run time, and reaches the two harness seams by name through reflection rather
than by referencing the harness. The type name, method name and signature are the contract,
pinned by unit tests on the CLI side and by a "keep in sync" note on the harness side. A
scenario assembly built against an older harness simply does not get the seam: summaries
stay stderr-only, which is the pre-feature behaviour rather than a crash.

## Consequences

- The assembly's harness version is the one that runs, always.
- A rename on the harness side breaks a test, not a build. The compiler cannot see the
  contract, so the pinning tests are the only thing standing between a rename and a silent
  regression in the field.
- Roughly 100 lines of lookup code and its tests exist purely to describe a call.

## Under review

`atlas stage` broke the premise. Since it shipped, `Atlas.Cli` references `Atlas` at compile
time with `Private=false`, `PrivateAssets=all` and `ExcludeAssets=runtime`, and `Atlas`
grants it friend access; `StageRunner` then calls `EngineStager` by compiled signature while
the real bytes are resolved from the target directory at run time. The repo therefore now
carries two mechanisms for one need.

The open question is whether all three seams should move onto the compile-time-only
reference, which deletes the lookup code and turns a rename into a build failure, or whether
"the CLI never references `Atlas.XUnit`" stays a line worth keeping, in which case this
record becomes accepted as written. Resolve before either mechanism grows a third user.

## Source files

- `src/Atlas.Cli/FixtureHarvest.cs:5`-`:20` and `IsolationSummaryHook.cs:5`-`:19`: the two
  seams and their name constants.
- `src/Atlas.XUnit/Internal/HostRegistry.cs:225`-`:235` and `IsolationSummarySink.cs:3`-`:12`:
  the harness side of both contracts, each with its keep-in-sync note.
- `src/Atlas.Cli/ScenarioAssemblyResolver.cs`: run-time resolution out of the scenario
  assembly's directory. `src/Atlas.Cli/Atlas.Cli.csproj:10`-`:12` states the rule.
- `src/Atlas.Cli/Atlas.Cli.csproj:34`-`:38`, `src/Atlas/Atlas.csproj:33`,
  `src/Atlas.Cli/StageRunner.cs`, `src/Atlas.Cli/StageAssemblyResolver.cs`: the
  compile-time-only reference that reopened the question.
