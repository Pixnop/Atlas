# Boot diagnostics as assertions

Date: 2026-09-23
Status: measured and implemented (this pass)
Tracks: a Discord report (public) that a mod author checks skill-tree assets parse by booting
Atlas, but a failed load only ever reached server-main.log, never the test result
Game versions measured: 1.21.7, 1.22.3, 1.22.7 (see "What this pass did not verify" below)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[0002-one-live-host-per-process](../adr/0002-one-live-host-per-process.md)

## Motivation

Atlas boots a real embedded server. Everything the engine itself checks at boot (JSON syntax,
property types, recipe ingredients resolving to a real item) it already checks for a mod under
test, and it already logs when something is wrong. None of that reached a scenario: the entries
went to `server-main.log` and nowhere else, so a mod author's "does it boot clean" check could
only ever be "does it boot at all", and a broken asset silently kept working (the engine degrades
gracefully: it logs and moves on) until someone happened to read the log by hand.

## Method

A throwaway fixture mod, `tests/BootDiagnosticsFixtureMod` (content-only, no C#, modeled after
`samples/SampleMod`), ships three assets shaped exactly like the three ways a mod author breaks
one by mistake:

- `blocktypes/malformed.json`: a blocktype JSON with a missing closing brace (invalid JSON).
- `blocktypes/badproperty.json`: a well-formed blocktype JSON whose `resistance` is a string
  (`"very hard indeed"`) instead of a number.
- `recipes/grid/missingitem.json`: a well-formed grid recipe whose one ingredient is
  `game:doesnotexistatall`.

Booted with `ServerHost` directly (an E2E test, no xUnit adapter needed for a research spike) and
the raw `server-main.log` read back, the engine logged one or two entries per case (five in all),
always at `Error` or `Warning`, and boot always continued past all three to `RunGame`:

```
[Error] Syntax error in json file 'bootdiagfixture:blocktypes/malformed.json': Failed
  deserializing malformed.json: Unexpected end when reading token. Path ''.

[Error] Exception thrown while trying to parse json data of the type with code
  bootdiagfixture:bootdiagbadproperty, variant bootdiagfixture:bootdiagbadproperty. Will
  ignore most of the attributes. Exception:
[Error] Exception: Could not convert string to double: very hard indeed. Path 'resistance'.
  <raw stack trace lines follow, no level prefix, part of the same LogException call>

[Warning] Failed resolving crafting recipe ingredient with code game:doesnotexistatall in
  Grid recipe
[Error] Grid Recipe with output 'game:stone-granite' contains an ingredient that cannot be
  resolved: Item code game:doesnotexistatall
```

Never `Fatal`: the engine's asset/recipe loader treats every one of these as "skip this asset,
keep going" (the mod still loads and starts; the block/recipe in question is just absent or
half-populated). This is what made the original report possible in the first place: the boot
"worked", so nothing failed.

### The engine hook

`Vintagestory.API.Common.ILogger` (public mod API, `ICoreAPI.Logger`) has an `EntryAdded` event:

```csharp
public event LogEntryDelegate EntryAdded; // (EnumLogType type, string message, object[] args)
```

Three things measured about it directly in-process (a temporary probe in the same E2E spike,
deleted after measurement) that shaped the design:

1. **`ServerMain.Logger` and `ICoreServerAPI.Logger` are reference-equal** (`ServerLogger`, on
   this engine): one subscription sees everything either name would.
2. **Every mod also gets its own `Mod.Logger`** (`Vintagestory.Common.ModLogger`, a distinct
   instance per mod), but a message logged through it still reaches
   `ServerMain.Logger.EntryAdded`, prefixed with `"[modid] "`. So a single subscription on
   `ServerMain.Logger` sees the engine's own boot-time logging (our three cases, all unprefixed)
   and anything a mod logs through its own logger, at any point in the host's life.
3. **`EntryAdded` fires with the raw format string, not the formatted message.** A probe call
   `mod.Logger.Warning("PROBE {0}", "hello")` was observed by `EntryAdded` as
   `"PROBE {0}"` verbatim, `args` carrying `["hello"]` separately. A consumer that wants the text
   `server-main.log` shows has to format it itself.

The subscription point: `ServerMain.Logger` is created in `ServerHost.BootServer` (via
`ConfigureEngineStatics`), before `PreLaunch()`/`Launch()`, the same place `ServerMain.Logger`
was already being assigned directly (`src/Atlas/Internal/Hosting/ServerHost.cs`). Subscribing
right after that assignment means the recorder is live before any asset is ever loaded; the
bridge mod's `StartServerSide` (the earliest point a scenario's own code could reach
`ICoreServerAPI.Logger`) runs long after asset loading is done, so there is no other way to see
these three cases from inside a scenario.

### What this pass did not verify

`ILogger` and `EntryAdded` are used directly, not through `EngineCompat`
(0003-engine-compatibility-by-shape-probing.md): they are public mod-facing API (the same surface
any shipping mod already calls `api.Logger.Warning(...)` through), not an internal field or
method whose shape is known to move between supported versions the way `Entity.Pos` or the exit
lifecycle do. `ServerMain.Logger` itself is already read and written directly, unguarded,
elsewhere in `ServerHost`; this only adds a subscription on the same static. The four
`BootDiagnosticsTests` were built and run against 1.21.7 and 1.22.7 as well as the 1.22.3 local
install, passing on all three with identical entry shapes; a decompile comparison also confirms
`ILogger.EntryAdded` and `LogEntryDelegate` are unchanged across the three. If a future engine
version changes the shape of `ILogger` itself, it is caught by ci.yml's newest-version lane and
the compat.yml sweep's watch above the floor (not the 1.21.7 floor lane by itself), the same net
every other direct API call in this codebase relies on.

The asset-path heuristic (below) is over free-form message text, not a structured field the
engine provides, so it is approximate by construction; see the `ponytail:` comment at its
definition for the upgrade path if that ever needs to be exact.

## Environmental noise (CI follow-up, 2026-09-23)

PR #143's E2E lane 1.21.7 shard C, run 35798661573, failed
`BootDiagnostics_Should_StayEmpty_When_NoModShipsBrokenAssets` on
`[Warning] Server overloaded. A tick took 791ms to complete.`: a clean boot with no mod under
test, on a loaded CI runner. `ServerMain` logs this itself whenever a tick takes too long; it says
nothing about any mod's assets.

To find every distinct message shape a clean boot can produce (not just this one), every
`server-main.log` kept by ci.yml's scratch sweep (`if: failure()`, so only a run with at least one
red class uploads it) was pulled and grepped for `[Warning]`/`[Error]`/`[Fatal]` lines between the
first log line and `Singleplayer Server now running!` (the ready line this engine actually emits;
"Dedicated Server now running" never appears, single-player embedded boot only):

- Run 35798661573 (this failure), `e2e-scratch-logs-1.21.7-C`: 41 scratch directories, one per
  test class the shard ran (the sweep keeps every class's directory when the job is red, not only
  the failing class's).
- Run 34033803349 (2026-09-06, pre-dates this feature but not the engine boot sequence),
  `e2e-scratch-logs-1.22.7`: 97 scratch directories, from an earlier CI shape (one shard, no
  fixture mod anywhere in the suite yet).

138 clean-boot windows in all. Every `Warning`/`Error`/`Fatal` line found across both, classified:

| Message shape | Level | Count | Class |
| --- | --- | --- | --- |
| `Server overloaded. A tick took {N}ms to complete.` | Warning | 9 (4 + 5) | Environmental: the machine, not the mod. Logged by `ServerMain` itself whenever a tick runs long; `N` was 791-2609 across the samples, uncorrelated with any mod or asset. |
| `Syntax error in json file '{modid}:{path}': ...` | Error | 3 | Real content diagnostic: `BootDiagnosticsFixtureMod`'s intentional malformed JSON, present only in classes that stage that fixture. |
| `Exception thrown while trying to parse json data of the type with code {modid}:{code}, variant {modid}:{code}. Will ignore most of the attributes. Exception:` + `Exception: Could not convert string to double: ... Path '...'.` | Error | 3 + 3 | Real content diagnostic: the fixture's wrong-typed property. |
| `Failed resolving crafting recipe ingredient with code {item} in Grid recipe` + `Grid Recipe with output Item code {item} contains an ingredient that cannot be resolved: Item code {item}` | Warning + Error | 3 + 3 | Real content diagnostic: the fixture's missing recipe ingredient. |

No third shape turned up: across 138 samples there is no "engine noise a clean vanilla boot always
emits" distinct from the tick warning above and the fixture's own intentional entries. The
`BootDiagnostics_Should_StayEmpty_When_NoModShipsBrokenAssets` guard (`Assert.Empty` on a boot
with no mod under test) already encoded that expectation; what was missing was that the tick
warning can appear on that same clean boot when the runner is loaded, which is exactly what
happened here.

**The rule.** `BootDiagnosticsLog.Add` discards a message matching
`^Server overloaded\. A tick took \d+ms to complete\.$` before it is ever recorded, the same way
it already discards anything below `Warning`. Not recorded with a flag `BootDiagnostics`/strict
mode then has to ignore: that would add a field to the public `BootDiagnosticEntry` record for a
distinction nothing outside this one filter needs to make, and every reader of
`World.BootDiagnostics` (a scenario, `FinishBoot`'s strict check, this test) would have to
remember to apply it. Dropping it at the source keeps `BootDiagnosticEntry` and
`IWorldSession.BootDiagnostics` exactly as simple as before this fix: they still mean "diagnostics
about the mod under test," full stop, and `StrictBootDiagnostics` can never fail for a reason that
has nothing to do with the mod's own assets, on any machine.
`tests/Atlas.Pure.Tests/Diagnostics/BootDiagnosticsLogTests.cs` pins this with the real measured
message shapes (791ms, 2609ms, the format-string form, and a near-miss that must still be kept).

## Design

**Recording.** A pure core, `Atlas.Internal.Diagnostics.BootDiagnosticsLog`, takes one raw
`(EnumLogType, rawMessage, args)` triple at a time and decides whether to keep it:

- Keep only `Warning`, `Error` and `Fatal`; every other level (`Chat`, `Event`, `StoryEvent`,
  `Build`, `VerboseDebug`, `Debug`, `Notification`, `Audit`) is noise for this purpose and
  discarded immediately.
- Format the message with its args (`string.Format`, falling back to the raw message when the
  placeholders and args disagree, since a malformed entry is still worth keeping over losing it).
- Discard the formatted message if it matches `EnvironmentalNoise`
  (`^Server overloaded\. A tick took \d+ms to complete\.$`, see "Environmental noise" above): the
  one message shape measured to be about the CI machine, not the mod under test.
- Split a `"[modid] "` prefix off into a `Source` (`"engine"` when there is none), matching the
  measured `ModLogger` shape.
- Pick out a best-effort `AssetPath`: the first `domain:token`-shaped substring in the message
  (`[a-z][a-z0-9_]*:[A-Za-z0-9_\-./]+`), or `null` when there is none. This is right for a message
  naming one asset and only approximate for one naming several: the grid-recipe `Error` line
  above mentions both the recipe's output (`game:stone-granite`) and its missing ingredient
  (`game:doesnotexistatall`); the heuristic picks whichever is mentioned first (the output, in
  that case). Marked `ponytail:` at its definition: acceptable because the companion `Warning`
  line from the same failure already names the actual missing ingredient, so the information is
  never lost, only sometimes on a different entry than expected.

Never unsubscribed, never cleared: the recorder lives for the whole host lifetime (one host per
scenario class, ADR-0002), so it keeps recording through the scenario too. That was a simplicity
choice, not a requirement with its own toggle: "stop recording at world-ready" would need extra
state for no benefit anyone asked for, since nothing about a scenario's own warnings piling up in
the same list is wrong; a scenario that only cares about the boot window can still filter by
reading the list right at the start of its body.

**Exposure.** `IWorldSession.BootDiagnostics` (`IReadOnlyList<BootDiagnosticEntry>`), matching the
existing read-only-list-of-records shape (`IClientObservations`). `BootDiagnosticEntry` is a
public record: `Level` (`EnumLogType`, reusing the engine's own enum; `IWorldSession` already
returns engine types directly, e.g. `Block`, `Entity`, `EnumReplaceMode`), `Source`, `Message`,
`AssetPath`.

**Strict mode.** `[AtlasWorld(StrictBootDiagnostics = true)]`, not an assembly-level option or a
new attribute: it is boot-scoped exactly like the world it configures (one host per class, the
same lifecycle every other `AtlasWorldAttribute` member already governs), and the closest existing
precedent, `AtlasScenarioAttribute.StrictIsolation`, lives at the same granularity as what it
makes strict (`RollbackWorld` is class-scoped too, via the shared host). An assembly-level switch
would force every scenario class in a project into the same choice, which is wrong for a project
whose classes stage different mods-under-test with different maturity. Off by default (default
behavior unchanged, the explicit requirement): a suite that never sets it keeps whatever entries
the engine logs, readable but non-fatal. When it is set and `BootDiagnosticsLog` holds anything by
the time the world is ready (the same checkpoint `FinishBoot` already uses to publish the host and
release the boot waiter; the recorder is checked immediately before that point, so a strict
failure never lets a scenario see a host that is about to die), `ServerHost` throws
`AtlasBootDiagnosticsException` with every offending entry listed, one per line. This is an
exception, not an outcome object
(0006-outcome-objects-on-expected-degrade-paths.md): 0006 reserves outcome objects for paths that
have a designed fallback (rollback degrades to a recycle); there is no fallback for "the assets
you shipped do not parse", so this follows 0006's own rule for the unexpected and fails the same
way `StrictIsolation` and a bridge-startup failure already do: a boot-time exception the xUnit
adapter turns into a failed class.

## What changed

- `Atlas.Api.BootDiagnosticEntry` (new public record): `Level`, `Source`, `Message`, `AssetPath`.
- `Atlas.Api.AtlasBootDiagnosticsException` (new public exception).
- `Atlas.Api.IWorldSession.BootDiagnostics` (new property) /
  `Atlas.Api.WorldOptions.StrictBootDiagnostics` (new property, default `false`).
- `Atlas.XUnit.AtlasWorldAttribute.StrictBootDiagnostics` (new property, default `false`), mapped
  by `AttributeMapper` exactly like `SaveFile` and the rest.
- `Atlas.Internal.Diagnostics.BootDiagnosticsLog` (new, internal, pure): the recording/filtering
  core, unit-tested without an embedded server.
- `ServerHost`: subscribes `BootDiagnosticsLog.Add` to `ServerMain.Logger.EntryAdded` right after
  the logger is created (`BootServer`), and fails the boot before publishing the host when strict
  mode is on and the recorder is non-empty (`FinishBoot`).
- `tests/BootDiagnosticsFixtureMod`: the fixture from the "Method" section above, kept (not
  actually thrown away) as the E2E fixture `BootDiagnosticsTests` stages.
- `samples/Sample.Scenarios/BootDiagnosticsScenarios.cs`: the realistic usage from the bug report
  itself: assert no `Error`-or-above entries for a mod that is supposed to boot clean.
- `Atlas.Internal.Diagnostics.BootDiagnosticsLog.Add` (2026-09-23 CI follow-up): discards the
  `Server overloaded` tick warning, the one message shape measured to be environmental rather
  than about the mod under test; see "Environmental noise" above.

## Consequences

- A mod author's "does it boot clean" check is now a real assertion, not a manual log read.
- Nothing about a suite that never touches this feature changes: the recorder always runs (the
  subscription itself is unconditional, and it costs one delegate call per logged entry of any
  level, since the level filter runs inside `Add`, not before it, immeasurable next to booting a
  server), but nothing reads or acts on it unless a scenario calls `World.BootDiagnostics` or a
  class opts into `StrictBootDiagnostics`.
- The `AssetPath` heuristic can point at the wrong one of several assets a single message names;
  `Message` always has the full text regardless, so nothing is hidden, only sometimes
  mis-highlighted.
- `ILogger`/`EntryAdded` being used directly instead of through `EngineCompat` is a bet that public
  mod API is stable enough not to need shape probing, consistent with how the rest of
  `WorldSession`/`ClientObservations` already call the engine's public surface directly; unlike
  `EngineCompat`'s own members it has no dedicated probe, relying instead on ci.yml's
  newest-version lane and the compat.yml sweep to catch a future break.
- `StrictBootDiagnostics` cannot fail because a CI runner (or any other machine) was busy during
  boot: the one measured environmental message shape is filtered before it is ever recorded, so a
  slow tick is invisible to both the default recorder and strict mode, the same as if the engine
  had never logged it.

## Source files

- `src/Atlas/Internal/Diagnostics/BootDiagnosticsLog.cs`: the recording/filtering core.
- `src/Atlas/Api/BootDiagnosticEntry.cs`, `AtlasBootDiagnosticsException.cs`: the public shapes.
- `src/Atlas/Internal/Hosting/ServerHost.cs`: the subscription (`BootServer`) and the strict check
  (`FinishBoot`, `DescribeStrictFailure`).
- `src/Atlas/Internal/Hosting/WorldSession.cs`: `BootDiagnostics`, threaded from the host.
- `src/Atlas.XUnit/AtlasWorldAttribute.cs`, `Internal/AttributeMapper.cs`: the declaration surface.
- `tests/Atlas.Pure.Tests/Diagnostics/BootDiagnosticsLogTests.cs`: the pure tests.
- `tests/BootDiagnosticsFixtureMod/`, `tests/Atlas.Engine.Tests/BootDiagnosticsTests.cs`: the E2E
  fixture and tests.
- `samples/Sample.Scenarios/BootDiagnosticsScenarios.cs`: the sample scenario.
