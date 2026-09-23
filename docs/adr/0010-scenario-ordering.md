# 0010. Scenario ordering within a class (issue #67)

Status: Proposed. The docs half of issue #67 has shipped (the wiki's RestartWorld example
now seeds at fixture boot with a no-ordering-guarantees callout, per the issue's own comment
thread). This record covers the design half only, and stops short of a decision: issue #67
frames first-class ordering as needing "a deliberate position rather than a reflex feature",
and that call belongs to the maintainer. What follows is what he needs to make it: the
measured facts, the options, and a labelled recommendation, not a decision already made.

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
  then `runner.Start()` with no type filter, so the whole assembly runs in one process.
- **`atlas run --worker --classes <names>`** (`WorkerRunner.cs:73,89`): the same
  `AssemblyRunner.WithoutAppDomain`, started with
  `TypesToRun = classes?.ToArray() ?? []` (xUnit's own `AssemblyRunnerStartOptions`), which
  scopes one process to an exact set of full class names and otherwise runs the same way.
- **`atlas run --parallel N`** (`ParallelRunner.cs`): discovers classes with
  `ScenarioDiscovery.Find` (`ParallelRunner.cs:31`), queues them in a plain FIFO
  `Queue<string>` (`ClassWorkQueue.cs:6-20`), and drains that queue with
  `ParallelDegree.Resolve(...)` worker subprocesses (`ParallelRunner.cs:38`,
  `ParallelDegree.cs:14-18`), each spawned as `atlas run <dll> --worker --classes <one class>`
  (`ParallelRunner.cs:118-122`, one class per subprocess invocation). A separate discovery
  path, `ScenarioDiscovery.Find` itself (`ScenarioDiscovery.cs:27-31`), sorts
  `.OrderBy(ClassName, Ordinal).ThenBy(DisplayName, Ordinal)` — but that ordering is only ever
  used for `--list` output and for building the class dispatch queue; it has no effect on
  method order within a class, which is decided by whichever `AssemblyRunner` instance
  actually executes that class.

### What order xUnit and `atlas run` actually use

None of the three paths above pass a `TestCaseOrderer` or `TestCollectionOrderer` of their
own. Ordering within a class is whatever xUnit's default discovery order produces, which
xUnit documents as unordered; Atlas adds nothing on top unless the class itself opts in (see
below). This matches the issue's own diagnosis and the maintainer's own comment confirming it
was reproduced and fixed at the docs level first.

### How `AlphabeticalOrderer` is wired in tests

Atlas's own suites solve the problem twice, independently, because `[TestCaseOrderer]` needs a
type name resolvable inside the SAME assembly as the class it decorates:

- `tests/Atlas.Engine.Tests/AlphabeticalOrderer.cs:9-14`: `ITestCaseOrderer` sorting test cases
  by `tc.TestMethod.Method.Name` by ordinal `StringComparer`. Applied to
  `AdapterRestartTests.cs:13`, `AdapterRollbackTests.cs:11`, `AdapterPlayerRollbackTests.cs:16`.
- `tests/Atlas.GuineaPig.Scenarios/AlphabeticalOrderer.cs:9-14`: byte-identical class, in the
  other test assembly. Applied to `IsolationActivityScenarios.cs:15`,
  `DeadHostSequenceScenarios.cs:19`.

Both are `internal`, live in test-only assemblies, and are not part of `Atlas.XUnit`'s public
surface, so a consumer cannot reference Atlas's copy; they would need to paste their own
14-line copy and their own `[TestCaseOrderer("Namespace.AlphabeticalOrderer", "AssemblyName")]`
per class. Five classes across Atlas's own two internal suites needed this, out of the whole
test tree, for scenarios whose own doc comments say the sequence is the point (for example
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
  `HostRegistry.RecycleAsync`). Order independent: every FreshWorld scenario starts from the
  same boot, regardless of what ran before it in the class.
- **RollbackWorld**: the world is restored to a snapshot "captured lazily, once per host, at
  the class's first rollback-enabled scenario (that scenario runs against the world as
  captured; later ones are rolled back to it)"
  (`src/Atlas.XUnit/AtlasScenarioAttribute.cs:21-23`), implemented in
  `HostRegistry.RollbackOrRecycleAsync` (`HostRegistry.cs:119-121`, capture-vs-restore split
  inside `ServerHost.TryRollbackWorldAsync`). This is order sensitive at exactly one boundary:
  whichever scenario xUnit happens to run first among the class's RollbackWorld-flagged
  methods silently becomes the fixture the rest are compared against. Nothing in the code
  detects or warns about this; it is the same class of bug issue #67 reports, on a different
  isolation mode, currently undocumented.
- **RestartWorld**: "the restart carries forward the CURRENT world state, mutations made by
  earlier scenarios included, not the original fixture"
  (`AtlasScenarioAttribute.cs:81-82`), via `AtlasTestInvoker.RestartWorldAsync`
  (`AtlasTestInvoker.cs:148-153`) and `HostRegistry.RestartAsync` (`HostRegistry.cs:173-176`).
  Order sensitive by design and documented as such; this is the mode issue #67's reproduction
  actually hit.

All four modes funnel through one static, per-process `HostRegistry` keyed by test class
(`HostRegistry.cs:12`, per ADR 0002), which is only coherent because every scenario assembly
declares `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
(`tests/Atlas.Engine.Tests/AssemblyInfo.cs:3`, `tests/Atlas.GuineaPig.Scenarios/AssemblyInfo.cs:3`,
`tests/Atlas.Fixture.Scenarios/AssemblyInfo.cs:3`; enforced at `HostRegistry.cs:285` for a
consumer assembly that forgets it). That attribute guarantees no two scenarios of the same
class ever run concurrently; it says nothing about which one runs first.

### What `--parallel` does to classes

`--parallel` never splits one class across two workers: `ClassWorkQueue` hands out whole class
names (`ClassWorkQueue.cs:14-20`), and each worker subprocess is invoked with exactly one
`--classes <name>` (`ParallelRunner.cs:120-122`), so a class's own scenarios still run
sequentially, in one process, under the exact same unordered-by-default `AssemblyRunner`
mechanism as plain `atlas run`. `[TestCaseOrderer]` on a class works identically under
`--parallel` as without it. What `--parallel` changes is CROSS-class order: classes are taken
off the FIFO queue by whichever worker frees up first (`ClassWorkQueue.cs:14-20`), so which
class finishes when is a function of worker availability, not declaration order, and several
classes are mid-execution at once. Nothing in the codebase couples a scenario's isolation mode
or ordering intent to another class, so this has no bearing on the RestartWorld/RollbackWorld
order sensitivity above, only on aggregate run time.

### Theory rows

`[AtlasTheory]` mirrors the same five settings "per data row" (each row is "a full scenario of
its own" — `src/Atlas.XUnit/AtlasTheoryAttribute.cs:10-11`). `AlphabeticalOrderer` sorts by
`TestMethod.Method.Name` (`AlphabeticalOrderer.cs:13` in both copies), which is identical for
every row of the same theory method, so it cannot express or preserve a row order; a stable
sort leaves rows of one method in whatever order xUnit's own theory-row discovery produced
them, which is the same "no guarantee" starting point as method order.

## Options

**1. No first-class ordering; document the pattern.** Keep the status quo: the wiki fix
(seed-at-boot, gated by config or `[AtlasDataFiles]`) is the general answer, and classes that
still want a strict sequence write their own orderer. *Consequences:* zero implementation
cost, zero new public surface, nothing to keep compatible later. Leaves every consumer to
either restructure around order-independence or hand-roll the ~14-line orderer Atlas already
wrote twice for itself. Does not touch the RollbackWorld first-scenario gap found above; that
would need its own documentation regardless of this decision. `--parallel` and theory rows are
unaffected because nothing changes.

**2. Ship the existing `AlphabeticalOrderer` as a public, opt-in `Atlas.XUnit` type.**
Promote the class Atlas already wrote and proved twice on itself
(`AlphabeticalOrderer.cs` in both `tests/Atlas.Engine.Tests` and
`tests/Atlas.GuineaPig.Scenarios`) so a consumer references it directly instead of pasting a
copy, opting in with one `[TestCaseOrderer("Atlas.XUnit.AlphabeticalOrderer", "Atlas.XUnit")]`
per class. *Consequences:* smallest new surface of the three feature options (no attribute
change, no `ScenarioSettings` change, no serialization change); consumers still couple
execution order to METHOD NAMES, so a rename silently reorders a chain with no compiler
signal, which is exactly the coupling issue #67 itself calls out as a cost; does nothing for
theory rows (previous section); orthogonal to `--parallel` (applies inside a single class,
unaffected by cross-class dispatch); does not address the RollbackWorld gap either, since that
needs a warning or documentation, not an orderer.

**3. An explicit `[AtlasScenario(Order = n)]`.** Add an integer knob read the same way
`TimeoutMs` is today, and a built-in orderer that sorts by it. *Consequences:* strongest
guarantee, decoupled from naming; touches the shared settings seam in five places
(`AtlasScenarioAttribute.cs`, its mirror `AtlasTheoryAttribute.cs`, plus
`ScenarioSettings.From`/`Read`/`Write` at `ScenarioSettings.cs:39-44,49-54,60-67`), so it is a
small but genuinely cross-cutting change, not a one-file addition; needs a documented rule for
ties and for mixing ordered with unordered scenarios in one class (silently falls back to
"unspecified" for the unordered ones, or refuses the mix — undecided); still requires the same
per-class `[TestCaseOrderer]` opt-in as option 2, so it removes the naming coupling but not the
opt-in step; class-scoped only, same as option 2 — an order value spanning two classes has no
meaning here, since `--parallel` already runs classes in separate processes and separate
hosts, and `HostRegistry` is one-live-host-per-process, so cross-class sequencing was never
achievable regardless of which option is picked. Does not by itself fix the RollbackWorld gap
unless that mode's snapshot-capture rule is changed to depend on `Order` too, which is a
second decision this record does not make.

**4. Ordering only for RestartWorld chains.** Scope any new ordering guarantee to scenarios
that opt into `RestartWorld` specifically (for example, only scenarios in the same class that
both set `RestartWorld = true` get a deterministic sequence, by declaration order or an
`Order` value attached to that flag), leaving SharedWorld and RollbackWorld out. *Consequences:*
narrowest surface, and it matches the one mode whose own doc comment already declares itself
order-sensitive by design (`AtlasScenarioAttribute.cs:81-82`) — the mode issue #67's
reproduction actually hit. But it leaves RollbackWorld's first-scenario-wins snapshot boundary
equally order-sensitive and unaddressed, which this investigation found to be the same class
of bug on a mode nobody has reported yet; picking this option answers today's report while
leaving a near-identical one latent. Smallest code change of the three feature options if
scoped to RestartWorld only, since `RestartWorld` already has its own dedicated resolver
branch (`AtlasTestInvoker.cs:72,148-153`) to hang a check off.

## Recommendation

Recommended: **option 2**, promoting `AlphabeticalOrderer` to `Atlas.XUnit` as a public,
opt-in type, and, independently of whichever option is picked, documenting the RollbackWorld
first-scenario-capture behavior (`AtlasScenarioAttribute.cs:21-23`) with the same explicitness
issue #67 already gave RestartWorld.

Reasoning: the coupling issue #67 itself warns about (ordering encourages coupled scenarios)
is real, and options 3 and 4 spend new public API surface to formalize that coupling before
there is evidence consumers need more than what Atlas needed for itself. Atlas's own suites
needed this for exactly 5 classes out of the whole test tree, and solved it every time with
the same 14-line orderer, copy-pasted because there was nowhere shared to put it, not because
the tool was insufficient. Shipping that existing, already-proven type removes the copy-paste
tax at effectively zero new surface, without committing to an `Order` attribute whose tie-
breaking and cross-class semantics are still open questions (option 3), and without building a
RestartWorld-only special case that leaves the RollbackWorld gap for the next reporter (option
4). If option 2 turns out not to hold, an `Order` value is a natural, additive follow-up on
the same settings seam (option 3's file list above) once a real consumer need shows it.

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
  `:135-140`, `:148-153`, `:165-179`: the three recycle/restart/rollback paths.
- `src/Atlas.XUnit/Internal/HostRegistry.cs:8-12`: one static host per process, keyed by class,
  relying on `DisableTestParallelization`; `:37`, `:119-121`, `:173-176`: the get-or-create,
  rollback and restart entry points; `:285`: the parallelization guard.
- `src/Atlas.Cli/ScenarioDiscovery.cs:27-31`: the alphabetical sort used only for `--list` and
  `--parallel`'s class queue, not for intra-class method order.
- `src/Atlas.Cli/ScenarioRunner.cs:25,48`: `atlas run`, one process, no type filter.
- `src/Atlas.Cli/WorkerRunner.cs:9,73,89`: `atlas run --worker --classes`, xUnit's own
  `TypesToRun` filter.
- `src/Atlas.Cli/ParallelRunner.cs:31,38,42,118-122`: `atlas run --parallel`, per-class worker
  subprocess dispatch.
- `src/Atlas.Cli/ClassWorkQueue.cs:6-20`: the FIFO class queue.
- `src/Atlas.Cli/ParallelDegree.cs:14-18`: worker count resolution.
- `tests/Atlas.Engine.Tests/AlphabeticalOrderer.cs:9-14` and
  `tests/Atlas.GuineaPig.Scenarios/AlphabeticalOrderer.cs:9-14`: the duplicated orderer;
  applied at `tests/Atlas.Engine.Tests/AdapterRestartTests.cs:13`,
  `AdapterRollbackTests.cs:11`, `AdapterPlayerRollbackTests.cs:16`,
  `tests/Atlas.GuineaPig.Scenarios/IsolationActivityScenarios.cs:15`,
  `DeadHostSequenceScenarios.cs:19`.
- `tests/Atlas.Engine.Tests/AssemblyInfo.cs:3`, `tests/Atlas.GuineaPig.Scenarios/AssemblyInfo.cs:3`,
  `tests/Atlas.Fixture.Scenarios/AssemblyInfo.cs:3`: `DisableTestParallelization`.
