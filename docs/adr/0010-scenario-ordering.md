# 0010. Scenario ordering within a class (issue #67)

Status: proposed. The docs half of issue #67 has shipped (the wiki's RestartWorld example
now seeds at fixture boot with a no-ordering-guarantees callout, per the issue's own comment
thread). This record covers the design half only, and stops short of a decision: issue #67
frames first-class ordering as needing "a deliberate position rather than a reflex feature",
and that call belongs to the maintainer. What follows is what the maintainer needs to make
it: the measured facts, the options, and a labelled recommendation, not a decision already
made.

## Context

Manifold's 0.7.0 dogfooding hit a seed-then-restart pair that ran reclaim-before-seed under
`atlas run`, because xUnit does not guarantee method execution order within a class. Atlas's
own test suites already lean on an internal orderer for exactly this reason. The rest of this
section is what the code actually does today, cited to file and line.

### How Atlas discovers and runs scenarios

`[AtlasScenario]` and `[AtlasTheory]` route through xUnit's own discovery pipeline:
`AtlasScenarioAttribute` declares `[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasScenarioDiscoverer", ...)]`
(`src/Atlas.XUnit/AtlasScenarioAttribute.cs:11`), and the discoverer yields one `AtlasTestCase`
per method, carrying the attribute's flags as a `ScenarioSettings` record
(`src/Atlas.XUnit/Internal/AtlasScenarioDiscoverer.cs:24-29`, settings built at
`src/Atlas.XUnit/Internal/ScenarioSettings.cs:39-44`). Nothing in this path assigns or reads
an order.

`src/Atlas.Cli` runs a compiled scenario assembly three ways, all built on xUnit's own
`AssemblyRunner`:

- **`atlas run`** (`ScenarioRunner.cs:25,48`): `AssemblyRunner.WithoutAppDomain(fullPath)`,
  then `runner.Start()` with no type filter, so the whole assembly runs in one process. A
  `TestCaseFilter` keyed on display name (`ScenarioRunner.cs:28`) can still narrow that to a
  subset, so `--filter` can run only half of an order-coupled pair and mask the other half.
- **`atlas run --worker --classes <names>`** (`WorkerRunner.cs:73,89`): the same
  `AssemblyRunner.WithoutAppDomain`, started with
  `TypesToRun = classes?.ToArray() ?? []` (xUnit's own `AssemblyRunnerStartOptions`), which
  scopes one process to an exact set of full class names and otherwise runs the same way (its
  own display-name filter at `WorkerRunner.cs:76` has the same masking effect as above).
- **`atlas run --parallel N`** (`ParallelRunner.cs`): discovers classes with
  `ScenarioDiscovery.Find` (`ParallelRunner.cs:31`), queues them in a plain FIFO
  `Queue<string>` (`ClassWorkQueue.cs:6-20`), and drains that queue with
  `ParallelDegree.Resolve(...)` worker subprocesses (`ParallelRunner.cs:38`,
  `ParallelDegree.cs:14-18`), each spawned as `atlas run <dll> --worker --classes <one class>`
  (`ParallelRunner.cs:118-122`, one class per subprocess invocation). A separate discovery
  path, `ScenarioDiscovery.Find` itself (`ScenarioDiscovery.cs:27-31`), sorts
  `.OrderBy(ClassName, Ordinal).ThenBy(DisplayName, Ordinal)`. That sorted list feeds `--list`
  (`ScenarioLister.cs:15`), `--parallel`'s class dispatch queue (`ParallelRunner.cs:31`),
  `atlas fixture`'s scenario selection (`FixtureRunner.cs:21`) and `--list --worker`
  (`WorkerLister.cs:20`); it has no effect on method order within a class, which is decided by
  whichever `AssemblyRunner` instance actually executes that class.

### What order xUnit and `atlas run` actually use

None of the three paths above pass a `TestCaseOrderer` or `TestCollectionOrderer` of their
own, so a class that does not opt in (see below) runs under whichever order xUnit's own
default orderer produces: `Xunit.Sdk.DefaultTestCaseOrderer`, which xUnit's own package docs
describe as producing "an unpredictable but stable order" (xunit.extensibility.execution
2.9.3, `Xunit.Sdk.DefaultTestCaseOrderer`), keyed on each test case's unique ID rather than
source position. Stable means one compiled assembly gives the same order every run, under
both `dotnet test` and `atlas run`; it is not source order, and xUnit gives no contract that
it stays the same after the assembly changes, so a method rename or an edit to a theory's
data can silently reorder an otherwise-untouched class. That is the actual shape of the risk
issue #67 names: not that the order varies run to run, but that it is unspecified and can
shift without warning.

### How `AlphabeticalOrderer` is wired in tests

Atlas's own suites solve the problem twice, independently, not because of an xUnit
limitation: `TestCaseOrdererAttribute`'s constructor takes `(ordererTypeName,
ordererAssemblyName)` precisely so the orderer can live in a different assembly from the
class it decorates (xunit.core 2.9.3, `Xunit.TestCaseOrdererAttribute`), and the same
attribute can decorate an assembly or a test collection, not only a class. The real reason
for two copies is that `tests/Atlas.Engine.Tests` and `tests/Atlas.GuineaPig.Scenarios` share
no project reference, so neither can point at the other's type:

- `tests/Atlas.Engine.Tests/AlphabeticalOrderer.cs:9-14`: `ITestCaseOrderer` sorting test cases
  by `tc.TestMethod.Method.Name` by ordinal `StringComparer`. Applied to
  `AdapterRestartTests.cs:13`, `AdapterRollbackTests.cs:11`, `AdapterPlayerRollbackTests.cs:16`.
- `tests/Atlas.GuineaPig.Scenarios/AlphabeticalOrderer.cs:9-14`: a second `public class
  AlphabeticalOrderer`, in the other test assembly. Matches the first copy only in its
  9-14 sort body; namespace (`:4`) and summary (`:6-8`) differ, each written for the class it
  serves. Applied to `IsolationActivityScenarios.cs:15`, `DeadHostSequenceScenarios.cs:19`.

Both are `public` types, but they live in test-only assemblies that `Atlas.XUnit` does not
ship, so a consumer cannot reference Atlas's copy; they would need to paste their own 14-line
copy and their own `[TestCaseOrderer("Namespace.AlphabeticalOrderer", "AssemblyName")]` per
class, or use the attribute's assembly-level form once, `[assembly: TestCaseOrderer(...)]`,
which reorders every class in that assembly whether it asked for ordering or not. Five
classes across Atlas's own two internal suites needed this, out of the whole test tree, for
scenarios whose own doc comments say the sequence is the point (for example
`DeadHostSequenceScenarios.cs:14`: "The orderer makes the A-then-B sequence deterministic").

### How the isolation modes interact with ordering

`WorldIsolationResolver.Resolve` picks exactly one of four modes per scenario
(`src/Atlas.XUnit/Internal/WorldIsolationResolver.cs:20-76`), dispatched in
`AtlasTestInvoker.InvokeTestMethodAsync` (`AtlasTestInvoker.cs:67-74`). They are not equally
order-sensitive:

- **SharedWorld** (no flags, the default): "the scenario runs against the class host's world
  as the previous scenario left it" (`src/Atlas.XUnit/Internal/WorldIsolation.cs:8-10`). Order
  sensitive by construction whenever two scenarios in a class touch the same world state; which
  one is "previous" is whatever xUnit happened to run first.
- **FreshWorld**: a full host recycle before the scenario (`AtlasTestInvoker.cs:70,135-140`,
  `HostRegistry.RecycleAsync` at `HostRegistry.cs:74`). Order independent: every FreshWorld
  scenario starts from the same boot, regardless of what ran before it in the class.
- **RollbackWorld**: the world is restored to a snapshot "captured lazily, once per host, at
  the class's first rollback-enabled scenario (that scenario runs against the world as
  captured; later ones are rolled back to it)"
  (`src/Atlas.XUnit/AtlasScenarioAttribute.cs:21-23`, which already documents WHERE the
  capture happens), implemented in `HostRegistry.RollbackOrRecycleAsync`
  (`HostRegistry.cs:119-121`), which calls `ServerHost.TryRollbackWorldAsync`
  (`ServerHost.cs:276-296`): the snapshot is taken from whatever state the world is in when
  the host's FIRST rollback request arrives, and every later rollback request against that
  host restores to it. Ordering among a class's own RollbackWorld-flagged scenarios does not
  move that snapshot, since each of them only captures once or restores. What does move it is
  a non-rollback scenario running before the capture (a SharedWorld scenario mutates the live
  world, and that mutated state is what the first RollbackWorld scenario then captures), or a
  new host resetting it (a FreshWorld or RestartWorld scenario replaces the live world, and a
  degraded rollback clears the snapshot outright, `ServerHost.cs:305`).
  `AdapterPlayerRollbackTests.cs:7-15` is the measured example: its SharedWorld scenario A
  joins a player and sets a baseline before its RollbackWorld scenario B captures, and B's own
  assertion (`:44`, "the player joined by A must still be connected") depends on that order.
  This is the same class of order sensitivity issue #67 reports, on a different isolation
  mode, and worth documenting with the same explicitness even though the capture point itself
  already is.
- **RestartWorld**: "the restart carries forward the CURRENT world state, mutations made by
  earlier scenarios included, not the original fixture"
  (`AtlasScenarioAttribute.cs:81-82`), via `AtlasTestInvoker.RestartWorldAsync`
  (`AtlasTestInvoker.cs:148-153`) and `HostRegistry.RestartAsync` (`HostRegistry.cs:173-176`).
  Order sensitive by design and documented as such; this is the mode issue #67's reproduction
  actually hit.

A class that goes dead adds a second, blunter way order decides an outcome: a scenario that
times out or crashes marks the class dead (`AtlasTestInvoker.cs:101,117`,
`HostRegistry.MarkDead`), and every later scenario in that class fails fast instead of
running its own body. `DeadHostSequenceScenarios` is built around exactly this: its ordered
crash scenario must run before its ordered fail-fast scenario for the second one to exercise
the path it asserts on.

All four modes funnel through one static, per-process `HostRegistry` keyed by test class
(`HostRegistry.cs:12`, per ADR 0002), which is only coherent because every scenario assembly
declares `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
(`tests/Atlas.Engine.Tests/AssemblyInfo.cs:3`, `tests/Atlas.GuineaPig.Scenarios/AssemblyInfo.cs:3`,
`tests/Atlas.Fixture.Scenarios/AssemblyInfo.cs:3`; enforced at `HostRegistry.cs:285` for a
consumer assembly that forgets it). What that attribute guarantees is CROSS-class: without
it, xUnit could run two different test classes (each its own default collection) against the
single static host at the same time; with it, classes run one at a time
(`HostRegistry.cs:8-11`: "so scenario classes run sequentially"). It says nothing about which
class, or which scenario inside a class, runs first, and it changes nothing about ordering
within one class: xUnit already runs one class's own test methods one after another
regardless of this attribute.

### What `--parallel` does to classes

`--parallel` never splits one class across two workers: `ClassWorkQueue` hands out whole class
names (`ClassWorkQueue.cs:14-20`), and each worker subprocess is invoked with exactly one
`--classes <name>` (`ParallelRunner.cs:120-122`), so a class's own scenarios still run
sequentially, in one process, ordered the same `DefaultTestCaseOrderer` way (previous section)
as plain `atlas run`. `[TestCaseOrderer]` on a class works identically under
`--parallel` as without it. What `--parallel` changes is CROSS-class order: classes are taken
off the FIFO queue by whichever worker frees up first (`ClassWorkQueue.cs:14-20`), so which
class finishes when is a function of worker availability, not declaration order, and several
classes are mid-execution at once. Nothing in the codebase couples a scenario's isolation mode
or ordering intent to another class, so this has no bearing on the RestartWorld/RollbackWorld
order sensitivity above, only on aggregate run time.

### Theory rows

`[AtlasTheory]` mirrors the same five settings "per data row" (each row is "a full scenario of
its own", `src/Atlas.XUnit/AtlasTheoryAttribute.cs:10-11`). `AlphabeticalOrderer` sorts by
`TestMethod.Method.Name` (`AlphabeticalOrderer.cs:13` in both copies), which is identical for
every row of the same theory method, so it cannot express or preserve a row order; a stable
sort leaves rows of one method in whatever order xUnit's own theory-row discovery produced
them, the same `DefaultTestCaseOrderer` starting point as method order (previous section):
stable from run to run, not source order, not guaranteed to survive a change to the theory's
data.

## Options

**1. No first-class ordering; document the pattern.** Keep the status quo: the wiki fix
(seed-at-boot, gated by config or `[AtlasDataFiles]`) is the general answer, and classes that
still want a strict sequence write their own orderer. *Consequences:* zero implementation
cost, zero new public surface, nothing to keep compatible later, couples nothing since
nothing ships. *atlas run / `--parallel`:* unaffected, nothing changes. *Theory rows:*
unaffected, same reason. *Existing suites:* Atlas's own two `AlphabeticalOrderer` copies and
any consumer's hand-rolled orderer keep working exactly as today. Leaves every new consumer
to either restructure around order-independence or hand-roll the ~14-line orderer Atlas
already wrote twice for itself, and leaves the RollbackWorld capture-order sensitivity found
above to be documented on its own, regardless of which option is picked.

**2. Ship the existing `AlphabeticalOrderer` as a public, opt-in `Atlas.XUnit` type.**
Promote the class Atlas already wrote and proved twice on itself
(`AlphabeticalOrderer.cs` in both `tests/Atlas.Engine.Tests` and
`tests/Atlas.GuineaPig.Scenarios`) so a consumer references it directly instead of pasting a
copy, opting in with one `[TestCaseOrderer("Atlas.XUnit.AlphabeticalOrderer", "Atlas.XUnit")]`
per class, or one `[assembly: TestCaseOrderer(...)]` to cover a whole assembly at once (with
the consequence noted above: every class in that assembly is reordered, asked for or not).
*Consequences:* one new public type; no attribute change, no `ScenarioSettings` change, no
serialization change; consumers still couple execution order to METHOD NAMES, so a rename
silently reorders a chain with no compiler signal, which is exactly the coupling issue #67
itself calls out as a cost. *atlas run / `--parallel`:* orthogonal to both, applies inside a
single class, unaffected by cross-class dispatch or which runner started it. *Theory rows:*
does nothing for them (previous section); a theory method still cannot express a row order
through this type. *Existing suites:* Atlas's own two copies become thin wrappers around, or
get deleted in favor of, the public type. Does not address the RollbackWorld capture-order
sensitivity either, since that needs a warning or documentation, not an orderer.

**3. An explicit `[AtlasScenario(Order = n)]`.** Add an integer knob and a built-in orderer
that sorts by it. *Design:* the minimal shape is a new `Order` property on
`AtlasScenarioAttribute` (mirrored on `AtlasTheoryAttribute`) plus an `ITestCaseOrderer` that
reads it straight off the reflected attribute, the same way `AlphabeticalOrderer` already
reads `Method.Name`; ordering happens at discovery time, before
`ScenarioSettings.From`/`Read`/`Write` (`ScenarioSettings.cs:39-44,49-54,60-67`) are ever
involved, so that seam does not have to change for ordering to work. A variant that also
threads `Order` through `ScenarioSettings` so the invoker can see it at runtime is possible,
but it is an addition for some other consumer, not a requirement of ordering itself; it would
touch all five places in that seam at extra cost for no ordering benefit. *Consequences:*
strongest guarantee, decoupled from naming; needs a documented rule for ties and for mixing
ordered with unordered scenarios in one class (falls back to "unspecified" for the unordered
ones, or refuses the mix, undecided); still requires the same per-class (or assembly-level)
`[TestCaseOrderer]` opt-in as option 2, so it removes the naming coupling but not the opt-in
step. *atlas run / `--parallel`:* class-scoped only, same as option 2; an order value
spanning two classes has no meaning here, since `--parallel` already runs classes in separate
processes and separate hosts, and `HostRegistry` is one-live-host-per-process, so cross-class
sequencing was never achievable regardless of which option is picked. *Theory rows:* every
row of one theory method shares that method's single `Order` value, the same limit
`AlphabeticalOrderer` already has on `Method.Name`, so this cannot express a row order either.
*Existing suites:* Atlas's own two `AlphabeticalOrderer` copies stay as they are unless
migrated to the new attribute by hand; nothing forces the migration. Does not by itself fix
the RollbackWorld capture-order sensitivity unless that mode's snapshot-capture rule is
changed to depend on `Order` too, which is a second decision this record does not make.

**4. Ordering only for RestartWorld chains.** Scope any new ordering guarantee to scenarios
that opt into `RestartWorld` specifically (for example, only scenarios in the same class that
both set `RestartWorld = true` get a deterministic sequence, by declaration order or an
`Order` value attached to that flag), leaving SharedWorld and RollbackWorld out. *Design:*
still needs a `TestCaseOrderer`, the same as options 2 and 3, since ordering is decided by
xUnit's class runner before `AtlasTestInvoker` ever runs a scenario
(`AtlasTestInvoker.cs:67-74`); a check hung off the RestartWorld branch there
(`AtlasTestInvoker.cs:72,148-153`) can only DETECT that the scenarios ran in the wrong order
and fail with a clear message, not enforce the right one. *Consequences:* narrowest surface of
the three feature options, and it matches the one mode whose own doc comment already declares
itself order-sensitive by design (`AtlasScenarioAttribute.cs:81-82`), the mode issue #67's
reproduction actually hit. But of Atlas's own five ordered classes, only `AdapterRestartTests`
is a RestartWorld chain at all; the other four order around a RollbackWorld capture
(`AdapterRollbackTests`, `AdapterPlayerRollbackTests`), a rollback before a restart
(`IsolationActivityScenarios`), or a crash before its fail-fast
(`DeadHostSequenceScenarios`). Scoping to RestartWorld only would leave 4 of Atlas's own 5
measured ordering needs, and the RollbackWorld capture-order sensitivity found above,
unaddressed. *atlas run / `--parallel`:* same as options 2 and 3, class-scoped, orthogonal to
`--parallel`'s cross-class dispatch. *Theory rows:* same per-method ceiling as option 3 if an
`Order` value is used; declaration order has the same ceiling implicitly. *Existing suites:*
none of Atlas's own five ordered classes could drop their `AlphabeticalOrderer` under this
option, since four of the five are not RestartWorld chains.

## Recommendation

Recommended: **option 2**, promoting `AlphabeticalOrderer` to `Atlas.XUnit` as a public,
opt-in type, and, independently of whichever option is picked, documenting the RollbackWorld
capture-order sensitivity found above (`AtlasScenarioAttribute.cs:21-23` already documents
WHERE the capture happens; it does not yet warn that a SharedWorld or RestartWorld scenario
running before it changes WHAT gets captured) with the same explicitness issue #67 already
gave RestartWorld.

Reasoning: the coupling issue #67 itself warns about (ordering encourages coupled scenarios)
is real, and options 3 and 4 spend new public API surface to formalize that coupling before
there is evidence consumers need more than what Atlas needed for itself. Atlas's own suites
needed this for exactly 5 classes out of the whole test tree, and solved it every time with
the same 14-line orderer, copy-pasted because the two test projects share no reference, not
because the tool was insufficient. Shipping that existing, already-proven type removes the
copy-paste tax at the cost of one small new public type (no new attribute, no settings
change), without committing to an `Order` knob whose tie-breaking and cross-class semantics
are still open questions (option 3), and without building a RestartWorld-only special case
that leaves 4 of Atlas's own 5 measured ordering needs, and the RollbackWorld sensitivity, for
the next reporter (option 4). If option 2 turns out not to hold, an `Order` value is a
natural, additive follow-up on the minimal design in option 3 once a real consumer need shows
it.

This is one reading of the trade-offs above, not the final call; the options and their
consequences are laid out so the maintainer can pick differently.

## Source files

- `src/Atlas.XUnit/AtlasScenarioAttribute.cs`: the five settings, discoverer wiring at `:11`,
  RollbackWorld's first-scenario capture rule at `:21-23`, RestartWorld's carry-forward rule at
  `:81-82`.
- `src/Atlas.XUnit/AtlasTheoryAttribute.cs:10-12`: the theory-row mirror ("each row is a full
  scenario of its own").
- `src/Atlas.XUnit/Internal/AtlasScenarioDiscoverer.cs:21-30`: one `AtlasTestCase` per method,
  settings attached at discovery.
- `src/Atlas.XUnit/Internal/ScenarioSettings.cs:39-44,49-54,60-67`: the settings seam a new
  knob would touch (`From`, `Read`, `Write`).
- `src/Atlas.XUnit/Internal/WorldIsolationResolver.cs:20-76`: resolves the four isolation
  modes and rejects contradictory flag combinations.
- `src/Atlas.XUnit/Internal/WorldIsolation.cs:8-25`: the four modes, SharedWorld's
  previous-scenario dependency at `:8-10`.
- `src/Atlas.XUnit/Internal/AtlasTestInvoker.cs:67-74`: the per-scenario isolation dispatch;
  `:135-140`, `:148-153`, `:165-179`: the three recycle/restart/rollback paths; `:101,117`:
  the two paths that mark a class dead (watchdog timeout, prior crash), which is why order
  decides which later scenario fails fast.
- `src/Atlas.XUnit/Internal/HostRegistry.cs:8-11`: "so scenario classes run sequentially",
  the comment behind `DisableTestParallelization`'s actual guarantee (cross-class, not
  intra-class); `:12`: one static host per process, keyed by class; `:37`, `:74`, `:119-121`,
  `:173-176`: the get-or-create, recycle, rollback and restart entry points; `:285`: the
  parallelization guard.
- `src/Atlas/Internal/Hosting/ServerHost.cs:276-296`: `TryRollbackWorldAsync`, the
  capture-on-first-request-then-restore split; `:305`: the degraded path that clears the
  snapshot.
- `src/Atlas.Cli/ScenarioDiscovery.cs:27-31`: the alphabetical sort, consumed by
  `ScenarioLister.cs:15` (`--list`), `WorkerLister.cs:20` (`--list --worker`),
  `ParallelRunner.cs:31` (`--parallel`'s class queue) and `FixtureRunner.cs:21` (`atlas
  fixture`'s scenario selection); none of the four apply it to intra-class method order.
- `src/Atlas.Cli/ScenarioRunner.cs:25,28,48`: `atlas run`, one process, no type filter, the
  display-name `TestCaseFilter` at `:28`.
- `src/Atlas.Cli/WorkerRunner.cs:9,73,76,89`: `atlas run --worker --classes`, xUnit's own
  `TypesToRun` filter, the matching display-name filter at `:76`.
- `src/Atlas.Cli/ParallelRunner.cs:31,38,42,118-122`: `atlas run --parallel`, per-class worker
  subprocess dispatch.
- `src/Atlas.Cli/ClassWorkQueue.cs:6-20`: the FIFO class queue.
- `src/Atlas.Cli/ParallelDegree.cs:14-18`: worker count resolution.
- `tests/Atlas.Engine.Tests/AlphabeticalOrderer.cs:9-14` and
  `tests/Atlas.GuineaPig.Scenarios/AlphabeticalOrderer.cs:9-14`: the duplicated orderer, public
  in both, matching only in this line range; applied at
  `tests/Atlas.Engine.Tests/AdapterRestartTests.cs:13`, `AdapterRollbackTests.cs:11`,
  `AdapterPlayerRollbackTests.cs:16`,
  `tests/Atlas.GuineaPig.Scenarios/IsolationActivityScenarios.cs:15`,
  `DeadHostSequenceScenarios.cs:19`.
- `tests/Atlas.Engine.Tests/AdapterPlayerRollbackTests.cs:7-15,44`: the measured
  RollbackWorld capture-order example (SharedWorld scenario A joins a player before
  RollbackWorld scenario B captures).
- `tests/Atlas.GuineaPig.Scenarios/DeadHostSequenceScenarios.cs:14,28,51`: the ordered
  crash-then-fail-fast pair.
- `tests/Atlas.Engine.Tests/AssemblyInfo.cs:3`, `tests/Atlas.GuineaPig.Scenarios/AssemblyInfo.cs:3`,
  `tests/Atlas.Fixture.Scenarios/AssemblyInfo.cs:3`: `DisableTestParallelization`.
- xUnit package docs, version 2.9.3 (resolved by this repo's `2.9.*` reference): `Xunit.Sdk.DefaultTestCaseOrderer`
  in `xunit.extensibility.execution`, `Xunit.TestCaseOrdererAttribute` and
  `Xunit.Sdk.ITestCaseOrderer` in `xunit.extensibility.core`.
