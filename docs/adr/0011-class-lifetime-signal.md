# 0011. Releasing the class host at the end of its class, through an xUnit class-lifetime signal

Status: accepted, implemented in 0.17.0 as option A2 below (issue #182). 0.16.0 shipped option C,
the timeout variable and the documentation that goes with it, while this record was still a
proposal. It qualifies the last sentence of ADR 0002 ("Atlas uses no xUnit class fixture
anywhere"): the registry still owns the one live host, and the one class fixture Atlas has is a
signal that owns nothing.

## Context

A class's host used to be released when the next class asked for a host (the hand-off) or, for the
last class, when the process exited (`HostRegistry`'s `ProcessExit` handler). Under `dotnet test`
the process exit was the problem: vstest kills the test host 100 ms after the session ends, and
the release takes about 0.9 s (0.90 to 0.91 s from a scenario's pass line to the process exit,
three runs on 1.22.3). The last class's scratch directory therefore survived a green run. On a
sample project with 12 scenarios, three runs without `VSTEST_TESTHOST_SHUTDOWN_TIMEOUT` left one
directory each (1.1 MB in the run that was sized) and three runs with it left none. The Pulse
suite reported about 7.5 MB per run (#182).

That is one directory per run, not one per class: a consumer's `dotnet test` run already swept
every class but the last. So the other #182 fixes, which cover Atlas's own tests and the CLI
paths, did not touch it, and a consumer who had not set the variable kept leaving that directory.

What was missing was a signal that a class has ended. The registry had none, by ADR 0002: it
learned that a class was over only from the next request or the process exit. xUnit has one, a
class fixture, disposed once after the last test of the class and inside the run, so before vstest
decides anything.

The release is also where `atlas fixture` can break. The command, and the workers of `atlas run
--parallel`, call `HostRegistry.ShutDownAndHarvestSavePathAsync` after the run (ADR 0007), and
that method used to expect the live host: it disposed it and returned the path of the save the
graceful shutdown persisted. If the class had already released it, there was nothing to harvest.
The CLI ships on its own cadence and Manifold installs it in CI (#170), so a CLI that predates the
change meets a harness that has it. Measured on a throwaway prototype with a CLI that sets no
retention switch (the 0.16 code is one, and so is 0.15.x): exit 1, no fixture written, and the
message "the builder scenario passed but left no world save to harvest (is it an [AtlasScenario]
on a class deriving from AtlasScenarioBase?)", which names the wrong cause.

## Decision

Options, from the least to the most code:

**C. Stay with the timeout variable.** Document `VSTEST_TESTHOST_SHUTDOWN_TIMEOUT=30000`, set it
in Atlas's own CI, and accept the leftover for runs without it. No code, no API change. Cost: the
default stays leaky, and the variable cannot be shipped for the consumer: the SDK's test targets
(10.0.111, `Microsoft.TestPlatform.targets`) have no property for the environment or the timeout,
so each shell and each CI job has to set it. A consumer's local `dotnet test` runs, the ones that
fill a tmpfs, are the ones least likely to.

**B. Release at the end of the class, with a switch the CLI sets and a documented CLI floor.**
`atlas fixture` sets `ATLAS_RETAIN_LAST_HOST`, the harness skips the release when it is set, and
the release notes say a 0.17 harness needs a 0.17 CLI. Smallest code of the three that release
early. Cost: the 0.15.x and 0.16 CLIs against a newer assembly fail as measured above, with a
misleading message, on a tool installed outside the project's own package graph.

**A. Release at the end of the class, with the harvest working without any signal from the
CLI.** Two ways to get there:

- A1. The harness detects fixture mode by looking at the process (entry assembly `Atlas.Cli`, a
  `fixture` argument) and skips the release. Works with every CLI. It puts knowledge of the CLI's
  command line into the harness, which ADR 0007 keeps out of it, and breaks silently on a rename.
- A2. The registry releases the host at the end of the class but remembers it, unswept. The
  harvest seam returns the remembered host's save when no host is live (the graceful release
  already persisted it), and the remembered host is swept at the next boot or at process exit.
  Nothing detects anything, and every CLI works.

**Accepted: A2.** It is the only option that closes the `dotnet test` leftover by default and
keeps `atlas fixture` working from every CLI. `AtlasScenarioBase` declares
`IClassFixture<AtlasClassLifetime>`; the fixture's dispose calls
`HostRegistry.ReleaseAtClassEndAsync`, which prints the class's isolation summary, disposes the
host gracefully and remembers it, scratch intact. `CreateAsync` (the next boot) and the
process-exit disposal sweep the remembered host under the same keep rules as any other disposed
host. A class marked dead keeps its host live and falls back to the old hand-off: its game thread
may be wedged, and releasing it at the class end would hold the run for the whole teardown bound.

## Consequences

- `Atlas.XUnit` gains a public type, `AtlasClassLifetime` (xUnit needs a public fixture type, and
  it has no public member), and `AtlasScenarioBase` gains an interface. A scenario class with its
  own constructor is unaffected; the samples, the guinea pig assemblies and the probe project run
  unchanged. A new API surface is an addition for the CHANGELOG, not a break.
- ADR 0002's ownership rule is unchanged: the registry still owns the one live host. The fixture
  is a signal and owns nothing, which is the part of "no class fixture" that no longer holds.
- The registry carries one more slot, the released host, next to the harvested hosts, with the same
  keep rules: a red class or a crashed host keeps its directory. It is never held alongside a live
  host, because every boot sweeps it first. The harvest seam takes the released host over, moving
  it to the harvested ones, so a second harvest finds nothing, as it does for a live host it
  already disposed. A stale released host would otherwise answer a harvest that ran no builder:
  the in-process `atlas fixture` tests hit exactly that on 1.21.7.
- The isolation summary prints when the class ends instead of at the next hand-off or at exit. The
  text does not change, and in the worker mode of the CLI the `class-summary` event arrives from
  the class end too.
- Measured on 1.22.3, `dotnet test` of the probe project without any timeout variable left one
  directory in 5 runs out of 5 before the change and none in 10 runs out of 10 after. What process
  exit has left to do is a delete (0.3 ms for a 1 MB, 15-file scratch tree), not a one-second
  release. `atlas fixture` with the unchanged CLI: exit 0, fixture written, no directory left.
  The same registry without the remembered host: exit 1, as above.
- The variable stays worth documenting for older assemblies and for a process killed before its
  exit sweep, which still leaves one directory. Atlas's own CI keeps it for the engine suite, whose
  classes call the registry by hand and so never end through the fixture.
- `ScratchSweepTests` follows the released host instead of the live one, and `ScratchHygieneTests`
  runs `dotnet test` without the variable. Before the change `ScratchSweepTests` would have passed
  without testing what it claims: its probes asked the registry for a host after the class ended,
  which boots a new one.
- Other xUnit v2 runners dispose class fixtures the same way; only vstest and the CLI's in-process
  runner were measured.

Not pursued: a custom xUnit test framework attribute (an assembly-level hook without a class
fixture, but it replaces the framework other extensions rely on), and marking the last test case
of a class at discovery (the last selected case is unknowable under `--filter`).

## Source files

- `src/Atlas.XUnit/AtlasClassLifetime.cs` and `AtlasScenarioBase.cs:10`: the fixture and the
  interface that attaches it to every scenario class.
- `src/Atlas.XUnit/Internal/HostRegistry.cs`: the process-exit hook at `:40`, the released host
  slot at `:34`, `GetOrCreateAsync` (the hand-off) at `:66`, the harvest seam at `:266`,
  `ReleaseAtClassEndAsync` at `:399`, `RememberReleased` at `:478`, the exit disposal at `:488`,
  `SweepReleased` at `:544`, `CreateAsync` (which sweeps it) at `:561`.
- `src/Atlas.Cli/FixtureRunner.cs:41` and `:59`: the harvest and the "left no world save" branch
  that an early release without the remembered host would reach. `src/Atlas.Cli/FixtureHarvest.cs`:
  the seam call. `src/Atlas.Cli/WorkerRunner.cs:131`: the workers' release through the same seam.
- `tests/Atlas.Engine.Tests/ScratchHygieneTests.cs`: the subprocess counts of directories that pin
  the result for every way of running scenarios. `tests/Atlas.Engine.Tests/ScratchSweepTests.cs`:
  the nested-class and by-hand release tests. `tests/Atlas.Pure.Tests/XUnit/ReleasedHostTests.cs`:
  the registry's rules for the released host, without a server.
