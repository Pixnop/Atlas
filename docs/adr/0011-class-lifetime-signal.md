# 0011. Releasing the class host at the end of its class, through an xUnit class-lifetime signal

Status: proposed. Nothing here is implemented: 0.16.0 ships the timeout variable of option C
and the documentation that goes with it (issue #182). This record exists so the owner can pick
between the options below, and so the one that breaks `atlas fixture` is not picked by accident.
If it is accepted, it qualifies the last sentence of ADR 0002 ("Atlas uses no xUnit class
fixture anywhere"), which stays accepted for everything else.

## Context

A class's host is released when the next class asks for a host (the hand-off) or, for the last
class, when the process exits (`HostRegistry`'s `ProcessExit` handler). Under `dotnet test` the
process exit is the problem: vstest kills the test host 100 ms after the session ends, and the
release takes about 0.9 s (0.90 to 0.91 s from a scenario's pass line to the process exit, three
runs on 1.22.3). The last class's scratch directory therefore survives a green run. On a sample
project with 12 scenarios, three runs without `VSTEST_TESTHOST_SHUTDOWN_TIMEOUT` left one
directory each (1.1 MB in the run that was sized) and three runs with it left none. The Pulse
suite reported about 7.5 MB per run (#182).

That is one directory per run, not one per class, and it is what the other #182 fixes do not
touch: they cover Atlas's own tests and the CLI paths, while a consumer's `dotnet test` run
already swept every class but the last. So without a change here, a consumer who has not set the
variable keeps leaving that directory.

What is missing is a signal that a class has ended. The registry has none, by ADR 0002: it
learns that a class is over only from the next request or the process exit. xUnit has one, a
class fixture, disposed once after the last test of the class and inside the run, so before
vstest decides anything. A throwaway prototype made `AtlasScenarioBase` implement
`IClassFixture<AtlasClassLifetime>`, whose `Dispose` asks the registry to release the host.

The release is also where `atlas fixture` breaks. The command, and the workers of `atlas run
--parallel`, call `HostRegistry.ShutDownAndHarvestSavePathAsync` after the run (ADR 0007), and
that method expects the live host: it disposes it and returns the path of the save the graceful
shutdown persisted. If the class already released it, there is nothing to harvest. The CLI ships
on its own cadence and Manifold installs it in CI (#170), so a CLI that predates the change will
meet a harness that has it. Measured on the prototype, with a CLI that sets no retention switch
(the 0.16 code is one, and so is 0.15.x): exit 1, no fixture written, and the message "the
builder scenario passed but left no world save to harvest (is it an [AtlasScenario] on a class
deriving from AtlasScenarioBase?)", which names the wrong cause.

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
the release notes say a 0.16 harness needs a 0.16 CLI. Smallest code of the three that release
early. Cost: the 0.15.x CLI against a 0.16 assembly fails as measured above, with a misleading
message, on a tool installed outside the project's own package graph. The harness could print a
line naming the cause, but the old CLI would still exit 1 and write no fixture.

**A. Release at the end of the class, with the harvest working without any signal from the
CLI.** Two ways to get there:

- A1. The harness detects fixture mode by looking at the process (entry assembly `Atlas.Cli`, a
  `fixture` argument) and skips the release. Works with every CLI. It puts knowledge of the CLI's
  command line into the harness, which ADR 0007 keeps out of it, and breaks silently on a rename.
- A2. The registry releases the host at the end of the class but remembers it, unswept. The
  harvest seam returns the remembered host's save when no host is live (the graceful release
  already persisted it), and the remembered host is swept at the next boot or at process exit.
  Nothing detects anything, and every CLI works.

**Recommendation: A2**, as the first change of 0.17 unless the owner accepts this record in time
for the 0.16 release candidate, and C until then. A2 is the only option that closes the
`dotnet test` leftover by default and keeps `atlas fixture` working from every CLI. It was
prototyped and measured on 1.22.3, throwaway, not committed:

- `dotnet test` on the 12-scenario sample, no timeout variable: no directory left in 6 runs out
  of 6. What process exit has left to do is a delete (0.3 ms for a 1 MB, 15-file scratch tree), not
  a one-second release.
- `atlas fixture` with the unchanged CLI: exit 0, fixture written, no directory left. The same
  prototype without the remembered host (option B's harness, without the switch): exit 1, as above.
- The classes that exercise the lifecycle (`ScratchSweepTests`, `NestedRunnerTests`,
  `FixtureCommandTests`, `RestartIsolationTests`, `IsolationObservabilityTests`) still pass, 21
  of 21. `ScratchSweepTests` passes without testing what it claims: as read from the code, its probes
  ask the registry for a host after the class ended, which now boots a new one.

## Consequences

If A2 is adopted:

- `Atlas.XUnit` gains a public type, `AtlasClassLifetime` (xUnit needs a public fixture type), and
  `AtlasScenarioBase` gains an interface. A scenario class with its own constructor is unaffected;
  the samples and the guinea pig assemblies ran unchanged on the prototype. A new API surface is
  an addition for the CHANGELOG, not a break.
- ADR 0002's ownership rule is unchanged: the registry still owns the one live host. The fixture
  is a signal and owns nothing, which is the part of "no class fixture" that no longer holds.
- The registry carries one more slot, the released host, next to the harvested hosts, with the
  same keep rules: a red class or a crashed host keeps its directory.
- The isolation summary prints when the class ends instead of at the next hand-off or at exit.
  The text does not change.
- `ScratchSweepTests` has to be rewritten around the class-end signal, and the dotnet-test rows of
  `ScratchHygieneTests` can drop the timeout variable. The variable stays worth documenting: a
  process killed before its exit sweep still leaves one directory.
- Measured on 1.22.3 only. Other xUnit v2 runners dispose class fixtures the same way, which
  was not measured.

If C stays the decision: the leftover is documented, and the consumer-side count of directories
after `dotnet test` is one per run unless the variable is set. Nothing else changes.

Not pursued: a custom xUnit test framework attribute (an assembly-level hook without a class
fixture, but it replaces the framework other extensions rely on), and marking the last test case
of a class at discovery (the last selected case is unknowable under `--filter`).

## Source files

- `src/Atlas.XUnit/Internal/HostRegistry.cs`: the process-exit hook at `:25`, `GetOrCreateAsync`
  (the hand-off) at `:41`, the harvest seam at `:236` and the harvested hosts it keeps at `:19`
  and `:255`, the exit disposal at `:317`, `SweepScratch` at `:452`.
- `src/Atlas.Cli/FixtureRunner.cs:41` and `:56`: the harvest and the "left no world save" branch
  that an early release would reach. `src/Atlas.Cli/FixtureHarvest.cs:19`: the seam call.
  `src/Atlas.Cli/WorkerRunner.cs:130`: the workers' release through the same seam.
- `src/Atlas.XUnit/AtlasScenarioBase.cs:6`: the base class that would carry the fixture.
- `tests/Atlas.Engine.Tests/ScratchHygieneTests.cs:17`: the subprocess counts of directories that
  pin every option's result. `tests/Atlas.Engine.Tests/ScratchSweepTests.cs:27`: the hand-off test
  that option A or B would hollow out.
