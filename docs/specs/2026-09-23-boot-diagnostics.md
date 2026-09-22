# Boot diagnostics as assertions

Date: 2026-09-23
Status: measured and implemented (this pass)
Tracks: a Discord report (public) that a mod author checks skill-tree assets parse by booting
Atlas, but a failed load only ever reached server-main.log, never the test result
Game versions measured: 1.22.3 (the local install; see "What this pass did not verify" below)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[0002-one-live-host-per-process](../adr/0002-one-live-host-per-process.md)

## Motivation

Atlas boots a real embedded server. Everything the engine itself checks at boot - JSON syntax,
property types, recipe ingredients resolving to a real item - it already checks for a mod under
test, and it already logs when something is wrong. None of that reached a scenario: the entries
went to `server-main.log` and nowhere else, so a mod author's "does it boot clean" check could
only ever be "does it boot at all", and a broken asset silently kept working (the engine degrades
gracefully: it logs and moves on) until someone happened to read the log by hand.

## Method

A throwaway fixture mod, `tests/BootDiagnosticsFixtureMod` (content-only, no C# - modeled after
`samples/SampleMod`), ships three assets shaped exactly like the three ways a mod author breaks
one by mistake:

- `blocktypes/malformed.json`: a blocktype JSON with a missing closing brace (invalid JSON).
- `blocktypes/badproperty.json`: a well-formed blocktype JSON whose `resistance` is a string
  (`"very hard indeed"`) instead of a number.
- `recipes/grid/missingitem.json`: a well-formed grid recipe whose one ingredient is
  `game:doesnotexistatall`.

Booted with `ServerHost` directly (an E2E test, no xUnit adapter needed for a research spike) and
the raw `server-main.log` read back, the engine logged exactly one entry per case, always at
`Error` or `Warning`, and boot always continued past all three to `RunGame`:

```
[Error] Syntax error in json file 'bootdiagfixture:blocktypes/malformed.json': Failed
  deserializing malformed.json: Unexpected end when reading token. Path ''.

[Error] Exception thrown while trying to parse json data of the type with code
  bootdiagfixture:bootdiagbadproperty, variant bootdiagfixture:bootdiagbadproperty. Will
  ignore most of the attributes. Exception:
[Error] Exception: Could not convert string to double: very hard indeed. Path 'resistance'.
  <raw stack trace lines follow, no level prefix - part of the same LogException call>

[Warning] Failed resolving crafting recipe ingredient with code game:doesnotexistatall in
  Grid recipe
[Error] Grid Recipe with output 'game:stone-granite' contains an ingredient that cannot be
  resolved: Item code game:doesnotexistatall
```

Never `Fatal`: the engine's asset/recipe loader treats every one of these as "skip this asset,
keep going" (the mod still loads and starts; the block/recipe in question is just absent or
half-populated). This is what made the original report possible in the first place - the boot
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
   instance per mod) - but a message logged through it still reaches
   `ServerMain.Logger.EntryAdded`, prefixed with `"[modid] "`. So a single subscription on
   `ServerMain.Logger` sees the engine's own boot-time logging (our three cases, all unprefixed)
   and anything a mod logs through its own logger, at any point in the host's life.
3. **`EntryAdded` fires with the raw format string, not the formatted message.** A probe call
   `mod.Logger.Warning("PROBE {0}", "hello")` was observed by `EntryAdded` as
   `"PROBE {0}"` verbatim, `args` carrying `["hello"]` separately. A consumer that wants the text
   `server-main.log` shows has to format it itself.

The subscription point: `ServerMain.Logger` is created in `ServerHost.BootServer` (via
`ConfigureEngineStatics`), before `PreLaunch()`/`Launch()` - the same place `ServerMain.Logger`
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
elsewhere in `ServerHost` - this only adds a subscription on the same static. This was measured
on 1.22.3 alone (the only install available); it was not cross-checked against 1.21.7 by
decompile or a live run the way the tick contract and pre-1.22 compat passes were. If a future
engine version changes the shape of `ILogger` itself, the 1.21.7 CI compat lane
(0003's enforcement mechanism) is the safety net, the same one every other direct API call in
this codebase relies on.

The asset-path heuristic (below) is over free-form message text, not a structured field the
engine provides, so it is approximate by construction; see the `ponytail:` comment at its
definition for the upgrade path if that ever needs to be exact.

## Design

**Recording.** A pure core, `Atlas.Internal.Diagnostics.BootDiagnosticsLog`, takes one raw
`(EnumLogType, rawMessage, args)` triple at a time and decides whether to keep it:

- Keep only `Warning`, `Error` and `Fatal`; every other level (`Chat`, `Event`, `StoryEvent`,
  `Build`, `VerboseDebug`, `Debug`, `Notification`, `Audit`) is noise for this purpose and
  discarded immediately.
- Format the message with its args (`string.Format`, falling back to the raw message if the
  placeholders and args disagree - a malformed entry is still worth keeping over losing it).
- Split a `"[modid] "` prefix off into a `Source` (`"engine"` when there is none), matching the
  measured `ModLogger` shape.
- Pick out a best-effort `AssetPath`: the first `domain:token`-shaped substring in the message
  (`[a-z][a-z0-9_]*:[A-Za-z0-9_\-./]+`), or `null` when there is none. This is right for a message
  naming one asset and only approximate for one naming several - the grid-recipe `Error` line
  above mentions both the recipe's output (`game:stone-granite`) and its missing ingredient
  (`game:doesnotexistatall`); the heuristic picks whichever is mentioned first (the output, in
  that case). Marked `ponytail:` at its definition: acceptable because the companion `Warning`
  line from the same failure already names the actual missing ingredient, so the information is
  never lost, only sometimes on a different entry than expected.

Never unsubscribed, never cleared: the recorder lives for the whole host lifetime (one host per
scenario class, ADR-0002), so it keeps recording through the scenario too. That was a simplicity
choice, not a requirement with its own toggle - "stop recording at world-ready" would need extra
state for no benefit anyone asked for, since nothing about a scenario's own warnings piling up in
the same list is wrong; a scenario that only cares about the boot window can still filter by
reading the list right at the start of its body.

**Exposure.** `IWorldSession.BootDiagnostics` (`IReadOnlyList<BootDiagnosticEntry>`), matching the
existing read-only-list-of-records shape (`IClientObservations`). `BootDiagnosticEntry` is a
public record: `Level` (`EnumLogType`, reusing the engine's own enum - `IWorldSession` already
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
release the boot waiter - the recorder is checked immediately before that point, so a strict
failure never lets a scenario see a host that is about to die), `ServerHost` throws
`AtlasBootDiagnosticsException` with every offending entry listed, one per line. This is an
exception, not an outcome object
(0006-outcome-objects-on-expected-degrade-paths.md): 0006 reserves outcome objects for paths that
have a designed fallback (rollback degrades to a recycle); there is no fallback for "the assets
you shipped do not parse", so this follows 0006's own rule for the unexpected and fails the same
way `StrictIsolation` and a bridge-startup failure already do - a boot-time exception the xUnit
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
  itself - assert no `Error`-or-above entries for a mod that is supposed to boot clean.

## Consequences

- A mod author's "does it boot clean" check is now a real assertion, not a manual log read.
- Nothing about a suite that never touches this feature changes: the recorder always runs (the
  subscription itself is unconditional, since it costs one delegate call per Warning-or-above
  entry engine-wide, immeasurable next to booting a server), but nothing reads or acts on it
  unless a scenario calls `World.BootDiagnostics` or a class opts into
  `StrictBootDiagnostics`.
- The `AssetPath` heuristic can point at the wrong one of several assets a single message names;
  `Message` always has the full text regardless, so nothing is hidden, only sometimes
  mis-highlighted.
- `ILogger`/`EntryAdded` being used directly instead of through `EngineCompat` is a bet that public
  mod API is stable enough not to need shape probing, consistent with how the rest of
  `WorldSession`/`ClientObservations` already call the engine's public surface directly; it has
  not been independently verified against 1.21.7 the way `EngineCompat`'s own members have.

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
