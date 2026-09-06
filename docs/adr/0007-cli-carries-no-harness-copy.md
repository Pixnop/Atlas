# 0007. The CLI carries no harness copy and calls the harness by compiled signature

Status: accepted.

## Context

`atlas` ships as its own dotnet tool, `Pixnop.Atlas.Cli`, on its own release cadence, while the
scenario assembly it runs ships the harness: `Atlas`, `Atlas.XUnit` and `AtlasBridge` sit in
that assembly's output directory, at whatever version the test project referenced. If the tool
carried its own copy, that copy would win at load time and the scenarios would run against a
harness the author never chose: version skew, silently.

Three CLI operations still need something from inside the harness. `atlas stage` runs the
staging decision the module initializers run at test-process boot, `atlas fixture` shuts the
builder scenario's host down gracefully and learns where the save landed, and worker mode
installs a sink so per-class isolation summaries reach the JSONL stream, not only stderr.

## Decision

The CLI resolves the harness out of the target's own directory at run time and calls it by
compiled signature. `Atlas.Cli` references `Atlas` and `Atlas.XUnit` with `Private=false`,
`PrivateAssets=all` and `ExcludeAssets=runtime`, and both grant it `InternalsVisibleTo`. The
compiler sees the signature; neither the packed tool nor the CLI's own output holds the bytes.

Each seam call sits in its own `[MethodImpl(NoInlining)]` method, invoked only once the
resolver is installed: JIT-compiling a method resolves every type it names, so nothing running
earlier may name the harness. Every call goes through `HarnessSeam.TryCall`, which turns a
signature mismatch into one diagnostic naming both versions, and exit 2.

## Consequences

- The assembly's harness version is the one that runs, always.
- A rename on the harness side is a build failure rather than a test failure, so the tests
  that pinned the call by name are gone, with the lookup code they described.
- A harness too old for a seam is the same diagnostic on all three commands, instead of a
  crash on one, exit 1 on another and silence on the third.
- Compatibility floor: `atlas fixture` and worker mode need a scenario assembly rebuilt
  against the same release's `Atlas.XUnit`, because the compiled call needs a friend grant the
  by-name lookup did not. `atlas stage` is unaffected, `Atlas` having granted the CLI friend
  access since 0.11.0, the release that introduced the command.

## Superseded alternative

The fixture and worker seams were originally reached by name through reflection, pinned by unit
tests on the CLI side and a "keep in sync" note on the harness side. That kept "the CLI never
references `Atlas.XUnit`" literally true, and let an older harness degrade rather than fail,
summaries staying stderr-only. `atlas stage` never used it, so one need carried two mechanisms.

## Source files

- `src/Atlas.Cli/HarnessSeam.cs:35`: `TryCall`, the one guard behind all three seams, with the
  two caller rules at `:14`. The seams: `FixtureHarvest.cs:1`-`:34`,
  `IsolationSummaryHook.cs:1`-`:45` and `StageRunner.cs:46` with `:67`. The harness side of the
  first two: `src/Atlas.XUnit/Internal/HostRegistry.cs:232`, `IsolationSummarySink.cs:19`.
- `src/Atlas.Cli/Atlas.Cli.csproj:33`-`:69`: the two compile-time-only references and the
  target stripping transitively resolved harness assemblies out of the CLI's output; the rule
  is stated at `:9`-`:13`, and the friend grants at `src/Atlas.XUnit/Atlas.XUnit.csproj:43`
  and `src/Atlas/Atlas.csproj:34`.
- `src/Atlas.Cli/ScenarioAssemblyResolver.cs`, `StageAssemblyResolver.cs`: the run-time side.
