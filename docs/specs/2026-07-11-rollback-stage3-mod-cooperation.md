# Rollback stage 3: mod cooperation, mini-dimension chunks, dirty-column filtering

Date: 2026-07-11
Status: proposed (design only, no implementation)
Tracks: issue #48 "future: rollback for mini-dimensions and dirty-column filtering (stage 3)"
Game version probed: Vintage Story 1.22.3 (net10.0), decompiled `VintagestoryLib.dll` /
`VintagestoryAPI.dll` (ILSpy 10.1)
Prerequisites: [world snapshot/rollback design](2026-07-06-world-snapshot-rollback.md)
(stage 1 shipped in 0.6.0, issue #2; observability shipped in 0.7.0)

## Problem

Stage 1 rollback restores the *world*: blocks, block entities, chunk-stored entities, chunk
moddata, the savegame blob and the calendar, for dimension 0 only. The base spec stated the
correctness boundary honestly and left one open question deliberately vague: "is 'your state
must follow chunk/entity lifecycle events' acceptable to the first consumers?"

Dogfooding on Manifold (21 scenarios on the `test/atlas-0.6-rollback-scenarios` branch,
harvested in issue #48) answered it: no, not for a whole class of mods. Three findings, in
increasing order of difficulty:

1. **Boot-time mini-dimensions poison eligibility.** A fixture mod that pregenerates
   mini-dimension terrain at boot makes *every* rollback of *every* class degrade to a full
   recycle ("loaded chunk in dimension 10... stage 1"), silently forfeiting the 23x-31x
   speedup. Manifold worked around it by moving pregeneration behind an on-demand command.
   Stage 3's acceptance bar: boot-time mini-dimensions must not disqualify rollback.
2. **Mini-dimension chunks need capture/restore.** Manifold's EntityTransit (chunk-stored
   entities) and BlockTransit (block entities) scenarios park world state in mini-dimension
   chunk columns; those columns must round-trip through the snapshot exactly as dimension-0
   columns do.
3. **Restoring SaveGame state desyncs a mod's in-memory registry from it.** Manifold's
   Ephemeral/Lifecycle scenarios mutate its dimension registry (ModSystem state) and its
   persisted manifest (SaveGame moddata) together. A rollback restores the manifest and not
   the registry, so the mod believes dimensions exist that the restored world no longer has,
   and vice versa. No world snapshot can fix this without the mod's help: stage 3 must ship
   either a rollback participation hook or a documented hard boundary.

This document designs the mod cooperation contract (findings 1 and 3), extends the snapshot
to mini-dimension columns (finding 2), and settles whether the dirty-column optimization is
worth building now. Target release: 0.8.0.

## Consumer evidence: Manifold, concretely

All paths below are in the Manifold repo (`src/Manifold`) and its Atlas dogfooding branch
(`tests/Manifold.Scenarios`). Manifold is the consumer this contract is grounded in; nothing
here is hypothetical.

**How Manifold splits its state between SaveGame and memory.** `ManifoldModSystem
.StartServerSide` builds a `DimensionRegistry` (an immutable-dictionary snapshot of
registered dimensions, ModSystem-owned, in memory) and a `DimensionAllocator` (the
`AssetLocation` to engine-dimension-id map, ids 10-1023, in memory). Persistence is a
manifest written into SaveGame moddata under `manifold:manifest`
(`SaveGameManifestStore.Write` calls `api.WorldManager.SaveGame.StoreData`), refreshed on
every `GameWorldSave` event, plus two more moddata blobs: `manifold:genchunks` (the set of
already-generated dimension columns, so revisits load instead of regenerate) and
`manifold:lastpos` (per-player per-dimension positions). At boot the registry is *seeded*
from the manifest: `DimensionPersistence.LoadOrEmpty()` feeds
`DimensionRegistry.SeedFromManifest(entry, state)`, which calls
`DimensionAllocator.ReserveSpecific(code, id)` so persisted ids survive restarts.

**The desync, step by step.** An Ephemeral scenario runs `/atlasfx create-ephemeral temp1`:
the fixture mod calls Manifold's registry, which allocates id (say) 10, adds `temp1` to the
in-memory snapshot, generates the dimension's spawn region (loading mini-dimension chunk
columns), and, being Ephemeral, never writes it to the manifest. Now roll the world back:

- The restored database no longer contains `temp1`'s chunk columns (correct).
- The restored `SaveGame.ModData` holds the pre-scenario manifest, genchunks and lastpos
  blobs (correct).
- Manifold's registry still contains `temp1` at id 10, the allocator still considers id 10
  reserved, `GeneratedColumnStore` still marks `temp1`'s columns as generated (so a future
  dimension reusing id 10 would *load* void instead of running worldgen), and the streaming
  worldgen driver still tracks it. The mod now believes in a dimension whose world no longer
  exists. The next `create-ephemeral` under the same code is refused as a duplicate.

The reverse desync exists too: `DimensionLifecycleScenarios` removes a dimension mid-
scenario; after a rollback the restored manifest and moddata describe state the in-memory
registry has already dropped and cleaned up (the `Destroyed` handler purges generator state,
positions and generated-column markers per dimension).

**What a correct Manifold resync does.** Everything needed already exists as boot-time code:
re-read the manifest from the restored SaveGame, rebuild the registry and allocator from it
(`SeedFromManifest` / `ReserveSpecific`), drop registrations absent from it, and re-run
`GeneratedColumnStore.LoadFromBytes` / `PlayerPositionStore.LoadFromBytes` on the restored
blobs. The cooperation contract's real demand on a mod is exactly this: *make your
boot-time hydrate path re-runnable*. Manifold's is about 30 lines of `StartServerSide`; the
resync is those lines refactored into a method that can run again.

**Assembly identity constrains the hook's shape.** The Manifold scenarios README documents
that scenario code cannot call the Manifold API directly: the game's ModLoader loads its own
copy of `Manifold.dll`, a distinct assembly instance whose statics and types are not the
ones the test assembly references. Atlas's own bridge (`BridgeModSystem`) documents the same
constraint from the other side and communicates through AppDomain data slots holding only
framework-typed delegates. Any hook that requires a mod to implement an Atlas-owned
interface inherits this problem: the interface type the harness checks against and the one
the mod compiled against would be different `Type` instances. The hook must be typed only in
things both sides share, which in this ecosystem means `VintagestoryAPI.dll` (loaded once,
from the game install) and framework types.

## Engine findings (decompile evidence)

New findings beyond the base spec's audit, from `VintagestoryLib.dll` 1.22.3. Type and
member names are the stable citation.

### Storage and the load path are already dimension-aware

`ChunkPos` (in `Vintagestory.Common.Database`) carries an explicit `Dimension` field packed
into the chunk index (`InternalY => Y + Dimension * 1024`; ids fit 10 bits, hence Manifold's
1023 ceiling). Database rows are keyed by that full index: `GameDatabase.GetChunk(x, y, z,
dim)` and `GetAllChunks()` round-trip dimension-keyed positions, which means **stage 1's
capture already snapshots mini-dimension rows** (a forced save persists every dirty loaded
chunk, dimension or not) and `DeleteExtraRows`' reconciliation by `ToChunkIndex()` is
already dimension-correct. The stage 1 guard rejects mini-dimensions only because the
unload/reload half of the recycle could not handle them.

The load path is public and dimension-parameterized end to end:
`IWorldManagerAPI.LoadChunkColumnForDimension(cx, cz, dim)` enqueues a
`ChunkColumnLoadRequest` with `request.dimension = dim`; the chunk thread reads
`gameDatabase.GetChunk(chunkX, i, chunkZ, request.dimension)`; the main-thread completion
(`ServerSystemSupplyChunks.mainThreadLoadChunkColumn`) inserts chunks at
`ChunkIndex3D(x, i + dimension * 1024, z)`, re-registers serialized entities through
`server.LoadEntity`, re-initializes block entities, and fires `TriggerChunkColumnLoaded`.
`IWorldManagerAPI.CreateChunkColumnForDimension` exists for creating *empty* columns (it
shares the dimension-0 `ServerMapChunk` at the same 2D coordinate, which extends stage 1's
documented map-chunk boundary unchanged: mini-dimensions add no new map chunks).

### Both unload paths are dimension-blind

`WorldAPI.UnloadChunkColumn(chunkX, chunkZ)` (stage 1's discard primitive) loops
`i < ChunkMapSizeY` over `ChunkIndex3D(chunkX, i, chunkZ)`: dimension 0 only, as the base
spec noted. The background unloader is equally blind: `ServerSystemUnloadChunks
.UnloadChunkColumns` iterates the same dimension-0 y range, so mini-dimension chunks are
never evicted by chunk churn; they stay loaded until shutdown. And at shutdown,
`OnBeginShutdown` fires `TriggerChunkColumnUnloaded` *only* for `Dimension <= 0`: the engine
never emits column-unloaded events for mini-dimension columns anywhere. Mods therefore
cannot be keying mini-dimension state to that event today, which frees stage 3 from having
to invent event semantics the engine itself does not have.

The discard mechanics are reusable, though: `ServerSystemUnloadChunks.TryUnloadChunk(long
posIndex3d, ChunkPos ret, ServerChunk chunk, List<ServerChunkWithCoord> dirtyChunksTmp,
ServerMain server)` is `public static`. It collects the chunk into the caller's dirty list
instead of persisting it, removes it from `loadedChunks`, queues the client unload packet,
and calls `ServerChunk.RemoveEntitiesAndBlockEntities` (public), which despawns non-player
entities with `EnumDespawnReason.Unload` and calls `OnBlockUnloaded()` on every block
entity: the same semantics `WorldAPI.UnloadChunkColumn` implements inline. Called with a
throwaway dirty list, it is exactly "discard one chunk without persisting", at any
dimension index.

Completion detection also needs a dimension-aware form: `ServerMain
.IsChunkColumnFullyLoaded(x, z)` checks the dimension-0 y range only, but
`ServerMain.GetLoadedChunk(long index3d)` is public, so "all chunks of column (x, z, dim)
present" is a trivial loop over the offset indices.

### The event bus: engine-native, synchronous, ordered, zero shared assemblies

`IEventAPI.PushEvent(string eventName, IAttribute data = null)` and
`RegisterEventBusListener(EventBusListenerDelegate, double priority = 0.5, string
filterByEventName = null)` are the engine's generic mod-to-mod signal. The server
implementation (`ServerEventAPI.PushEvent`) iterates listeners *synchronously on the calling
thread*, in descending priority order, with no try/catch: a listener exception propagates to
the pusher. `EnumHandling.PreventSubsequent` can stop the chain. Payloads are `IAttribute`
(in practice `TreeAttribute`), a VintagestoryAPI type both sides share by construction.

For a rollback hook this is a near-perfect fit: Atlas holds the live `ICoreServerAPI` (via
the bridge rendezvous) and already runs capture/restore on the game thread, so a
`PushEvent` from inside `WorldSnapshot` runs every subscribed mod's handler inline, in a
deterministic order, before the next restore step, and a throwing handler lands in the
existing fail-closed catch in `ServerHost.TryRollbackWorldAsync`.

### `DirtyForSaving` is a real per-chunk changed signal, with known setters

`ServerChunk.DirtyForSaving` (public field) is set by `MarkModified()` (block writes, block
entity changes, moddata writes) and by `AddEntity` / `RemoveEntity` (so an entity crossing
chunks dirties both sides). It is the signal the engine's own save uses to decide what to
persist, which makes it the candidate filter for a restore that skips untouched columns.
What it does not cover is exactly stage 1's existing boundary (in-memory map chunk state)
plus any hypothetical mod writing chunk internals without `MarkModified`, which would be a
mod bug by the engine's own persistence rules (such a change would not survive a normal
save either).

## Design question 1: the hook's shape

### Options

**(a) An interface in an Atlas-owned assembly, implemented by a ModSystem.** Discovery by
scanning `api.ModLoader.Systems` for the interface. Rejected on two independent grounds.
First, assembly identity: the ModLoader loads staged mod DLLs as distinct assembly
instances (see consumer evidence above), so the `IRollbackParticipant` the harness type-
checks against and the one the mod compiled against are different types unless the contract
DLL is loaded exactly once from exactly one place, which nothing in the mod-loading pipeline
guarantees. Second, dependency direction: it forces a *shipping* mod to reference a *test
harness* package to be testable, the wrong coupling even when it works.

**(b) Reflection-by-name (duck typing).** Scan ModSystems for a magic method name like
`OnAtlasRollbackRestored`. Dodges assembly identity, but the contract becomes stringly-typed
method signatures with no compile-time checking, silent no-op on a typo or signature drift,
and awkward versioning. Rejected: it reinvents an event bus, badly, when the engine ships a
real one.

**(c) The engine event bus.** Atlas pushes named events with a `TreeAttribute` payload;
mods subscribe with `RegisterEventBusListener(..., filterByEventName: ...)`. No discovery
problem (the bus filters; Atlas pushes unconditionally, and pushing to zero listeners is
near-free), no shared assembly (the contract is a documented event name plus payload shape,
typed entirely in VintagestoryAPI), deterministic ordering (priority), synchronous execution
on the game thread mid-restore (exactly where the resync must happen), and exception
propagation straight into the existing degrade machinery.

**(d) A bridge-raised C# event via AppDomain slots**, the way the bridge itself talks to
Atlas. Workable, but every participating mod would need to know Atlas's slot names and
delegate shapes: a bespoke private protocol where the engine already ships a public one.
Rejected.

### Recommendation: option (c), two events, one degrade reason

Event names and payloads (schema versioned inside the payload, `version = 1`):

- **`atlas:rollback:captured`**: pushed once per capture, immediately after the snapshot is
  in memory (end of `CaptureAsync`). Payload: `version` (int), `generation` (int,
  increments per capture). Purpose: a mod whose in-memory state is *not* derivable from
  SaveGame data can pair its own cheap in-memory snapshot with Atlas's. Most mods
  (Manifold included) can ignore this event entirely.
- **`atlas:rollback:restored`**: pushed on every restore, after the database blobs and the
  live `SaveGame` globals (moddata included) are restored, and *before* any chunk column is
  reloaded (ordering rationale under question 3). Payload: `version` (int), `generation`
  (int, matching the capture), `restoreCount` (int, per generation). The handler's job:
  rebuild in-memory state from the now-restored SaveGame, exactly as at boot.

The payload deliberately does not hand out the `SaveGame` instance: mods read it through
their normal `api.WorldManager.SaveGame` access, which is already the restored object
(stage 1 restores globals in place on the live instance). A capture token (the
`generation`/`restoreCount` pair) is included for idempotence and logging, not correctness.

Ordering across mods: bus priority. Convention documented with the contract: library mods
that other mods build on (Manifold) subscribe above the 0.5 default (e.g. 0.6) so their
state is coherent before their consumers' handlers run, mirroring `ExecuteOrder` at boot.

Failure mapping: `PushEvent` propagates a handler exception synchronously into
`TryRollbackWorldAsync`'s existing catch. Add `RollbackDegradeReason.ModHookFailed`
(described as "mod rollback hook failed"), classified from a wrapper exception thrown around
the push so the mod's exception type and message land in the one-line degrade detail. The
fail-closed contract is unchanged and remains safe: when a restored-hook throws, the world
database is already restored but some mod's in-memory state is unknown, and the fallback
full recycle rebuilds everything from scratch. `StrictIsolation` turns the degrade into a
scenario failure carrying the reason, via the machinery 0.7.0 already ships (test-output
report, stderr warning, per-class isolation summary); the summary gains the new reason in
its breakdown for free since it enumerates `RollbackDegradeReason`.

One listener throwing stops later listeners (engine behavior, no try/catch per listener).
Acceptable under fail-closed: the recycle resets every mod regardless of who ran.

## Design question 2: the no-cooperation default

Heuristics considered for detecting a mod that needs the hook but does not subscribe:

- **Flag mods whose `SaveGame.ModData` keys changed during the scenario**: unsound in both
  directions. A mod that follows chunk/entity lifecycle events may write moddata and be
  perfectly rollback-clean (Manifold's fixture publishes results through moddata); a mod
  with a stale in-memory accumulator may write nothing during the scenario at all.
- **Reflect over ModSystem fields and diff before/after**: no way to know which fields are
  semantically world-derived, which are caches, which are handles; touching private state of
  arbitrary mods to guess is exactly the kind of magic Atlas's fail-closed design avoids.
- **Degrade whenever any mod without a listener is loaded**: vanilla alone loads dozens of
  ModSystems that will never subscribe; this disables rollback universally and makes the
  feature pointless.

None are sound, so stage 3 does not ship heuristics. The recommendation is the hard
boundary, documented where scenario authors already read (the `RollbackWorld` XML docs and
the wiki): mods whose state follows chunk/entity lifecycle events stay coherent through a
rollback; mods with registry-style in-memory state keyed to SaveGame data need the
`atlas:rollback:restored` hook; scenarios exercising mods that have neither use
`FreshWorld`. Two things make this boundary livable rather than a trap. First, the audience:
rollback is a test-time contract, and the person opting a scenario into `RollbackWorld` is
typically the author of the mod under test, the one person who knows its state model.
Second, 0.7.0's observability: nothing degrades silently, every fallback names its reason
per scenario and per class, and `StrictIsolation` turns a violated speedup expectation into
a red test.

## Design question 3: mini-dimension chunks in the snapshot

### What changes, per rollback phase

**Capture.** `WorldSnapshot.LoadedColumns()` stops throwing on `Dimension != 0` and records
columns as `(X, Z, Dimension)` triples (source: the same `LoadedChunkIndices` walk;
`ChunkPos` already decodes the dimension). The database reads need no change: the blobs are
dimension-keyed already. `RollbackDegradeReason.MiniDimensionChunksLoaded` becomes
unreachable and is deleted (it is internal; nothing external names it).

**Unload (the restore's discard pass).** Dimension-0 columns keep the public
`WorldAPI.UnloadChunkColumn` path. For each loaded column with `Dimension != 0`, Atlas
replicates the discard using the engine's own public pieces, per chunk index
`ChunkIndex3D(x, dim * 1024 + i, z)`: fetch via `GetLoadedChunk`, then either call the
public static `ServerSystemUnloadChunks.TryUnloadChunk` with a throwaway dirty list, or
inline the same three steps (`RemoveEntitiesAndBlockEntities`, remove from `loadedChunks`,
dispose). The stage 0 style equality test decides between the two forms; the semantics are
identical by the decompile. Deliberately *not* fired: `TriggerChunkColumnUnloaded` for
mini-dimension columns, matching the engine, which never fires it for `Dimension > 0`
anywhere (see engine findings). Mods cannot be relying on an event that does not exist; the
cooperation hook is the designed signal instead. The existing post-unload assertion
(`LoadedChunkIndices.Length == 0`) already covers all dimensions.

**Restore ordering: the hook fires between state restore and chunk reload.** This is the
load-bearing ordering decision, driven by Manifold: when a mini-dimension chunk column
reloads, `TriggerChunkColumnLoaded` fires and entities re-register, and from that moment
mod code (chunk-loaded handlers, Manifold's streaming worldgen driver on its next tick,
worldgen strategies for any follow-up generation) consults the mod's registry. If the
registry still holds pre-rollback state, those consultations see ghosts. So the sequence is:

1. Wait for save-idle (unchanged).
2. Unload all columns, all dimensions (discard paths above).
3. Restore database blobs; `DeleteExtraRows` (already dimension-correct).
4. Restore `SaveGame` globals in place, including `MiniDimensionsCreated` (unchanged).
5. **Push `atlas:rollback:restored`.** Manifold rebuilds its registry, allocator and
   stores from the restored manifest here, while no mini-dimension chunk is loaded: its
   dimensions are registered before their world reappears.
6. Reload dimension-0 columns (`LoadChunkColumnPriority`, `KeepLoaded`, unchanged) and
   mini-dimension columns (`LoadChunkColumnForDimension(x, z, dim)`, the public
   dimension-aware request; no `KeepLoaded` needed since the background unloader never
   evicts `Dimension != 0` chunks).
7. Pump until complete: `IsChunkColumnFullyLoaded` for dimension 0, a `GetLoadedChunk`
   loop over the offset indices for the rest.

The alternative order (reload first, then notify) was considered and rejected: it would
make step 6's `TriggerChunkColumnLoaded` handlers and any tick between reload and hook run
against desynced mod state, which is precisely the bug class stage 3 exists to close.

**The acceptance bar from the harvest.** With capture covering all dimensions, a fixture
that pregenerates mini-dimensions at boot no longer degrades anything: the pregenerated
columns are simply part of the snapshot. The on-demand pregen pattern (`/atlasfx pregen`)
stays documented as a snapshot-size optimization, not a requirement.

## Design question 4: dirty-column filtering

The numbers, from the stage 0 spike (issue #2 verdict) and 0.6.0 measurements: a full
restore costs ~180 ms against a ~4.85 s recycle (27x; shipped stage 1 measured 23x-31x
across runs) on the ~50-column baseline world. Dirty-column filtering would shrink only the
unload/rewrite/reload portion of those 180 ms, bounded below by the forced-save wait, the
database round trip for the savegame blob, and the tick pumping. Best case (a scenario that
touched 2-3 columns) is perhaps 120-150 ms saved per rollback. A 21-scenario suite like
Manifold's saves ~3 s per run, on runs that cost minutes; against that stands a real
correctness risk surface: trusting `DirtyForSaving` as a complete change signal, a baseline
reset pass at capture, and a reconciliation path for columns whose database row set changed
without the flag. Stage 1's track record argues for restraint: its correctness came from
using the engine's coarse, battle-tested round trip wholesale.

Two stage 3 facts do shift the calculus slightly. Mini-dimension coverage grows snapshots
(a Manifold fixture with four boot-pregenerated dimensions at radius 2 adds up to ~100
columns, plausibly pushing restores toward 400-500 ms), and `DirtyForSaving`'s setters are
now audited (`MarkModified`, `AddEntity`/`RemoveEntity`: block, block entity, moddata and
entity-membership changes are all covered, and anything that bypasses them would not survive
a normal save either, making it a mod bug, not a rollback bug).

Recommendation: **do not build it in 0.8.0; instrument for it.** Stage 3a adds two cheap
numbers to the per-class isolation summary: measured restore cost, and dirty columns at
restore versus total columns restored. If real consumer suites show restores past ~500 ms
with low dirty ratios, implement the filter behind the existing fail-closed machinery
(any inconsistency detected between the dirty set and the database diff degrades that
restore to the full-column path, not to a recycle). The design slots into `RestoreAsync`
without new API surface, which is exactly why deferring it costs nothing.

## Design question 5: versioning, packaging, and where the listener lives

What does a mod reference to be rollback-cooperative? The options:

**(a) A new contract package (`Pixnop.Atlas.Contracts`).** A tiny assembly with the
interface or event-args types. Rejected: it inherits the assembly-identity hazard of
question 1(a) (the ModLoader loads staged copies as distinct assembly instances, so shared
types are only shared if loading is centralized, which it is not), adds a NuGet package to
version-manage forever, and makes shipping mods depend on test infrastructure.

**(b) The hook types ship in `Pixnop.Atlas` (the harness).** A non-starter: the harness
assembly is not loaded inside the game's mod space at all (only the staged bridge is), so a
mod cannot reference its types at runtime even before identity problems.

**(c) No assembly at all: the contract is the event name and payload shape.** With the
event-bus hook, a participating mod references nothing beyond `VintagestoryAPI.dll`, which
it already references by being a mod. The contract surface Atlas ships is documentation
(wiki page plus a copy-paste listener snippet) and a `version` field inside the payload for
schema evolution. Atlas pushes the events directly through its rendezvous-captured
`ICoreServerAPI`; even the bridge needs no change. A mod's listener is inert outside Atlas
runs: the events simply never fire in production, so carrying the subscription costs a
string comparison per unrelated bus event, and mods that prefer zero production footprint
can register it from their test fixture mod instead (below).

**(d) The scenario (not the mod) declares a post-restore resync action.** The alternative
the "it is a test concern" instinct suggests: no hook at all; instead the scenario or class
declares an action Atlas runs after each restore, e.g.
`[AtlasScenario(RollbackWorld = true, PostRollbackCommand = "/atlasfx resync")]`, with the
fixture mod implementing the resync through the mod's public API. Compared honestly:

- *For it*: zero contract in the mod, zero new event plumbing, cooperation lives entirely
  in the suite that wants it, and it composes with the existing fixture-mod pattern.
- *Against it, decisively*: ordering. A command (or any scenario-level action) runs after
  the restore completes, which is after mini-dimension columns reload (question 3's
  sequence), so chunk-loaded handlers and driver ticks still run against desynced state in
  the window; fixing that means Atlas would have to split its restore around an injected
  action anyway, arriving at the hook with extra steps. Second: capability. Manifold's
  resync needs `SeedFromManifest` / `ReserveSpecific` / store reloads, none of which are
  (or should be) public API; a fixture-side resync would force Manifold to expose a
  "rebuild yourself" public method, which is the hook again, wearing a command. Third:
  it is per-scenario opt-in and forgettable; the event fires for every restore
  automatically.

Recommendation: **(c)**, with placement guidance instead of a placement rule. The listener
belongs in the shipping mod when the resync needs internals (Manifold: ~15 lines in
`ManifoldModSystem`, refactoring its boot hydrate into a re-runnable method, referencing
only engine types). It belongs in the suite's fixture mod when the mod's public API can
rebuild the state (then the shipping mod carries nothing at all). Both placements implement
the identical contract, so a mod can start fixture-side and move the listener inward when
it grows internal state. Option (d)'s attribute is not shipped: any suite that wants a
scenario-scoped action can already run a command explicitly at the top of the scenario
body, and the restore-ordering slot is the hook's alone.

## Recommendation summary and staged plan for 0.8.0

1. **Hook shape**: engine event bus; `atlas:rollback:captured` and
   `atlas:rollback:restored` pushed by `WorldSnapshot` on the game thread;
   `TreeAttribute` payload with `version`/`generation`/`restoreCount`; handler exceptions
   degrade fail-closed under a new `RollbackDegradeReason.ModHookFailed`, strict mode
   fails the scenario.
2. **No cooperation**: hard boundary, documented; no heuristics (none are sound).
3. **Mini-dimensions**: capture all dimensions; discard `Dimension != 0` columns via the
   engine's public unload pieces; hook fires after state restore, before any column
   reload; reload via `LoadChunkColumnForDimension`; dimension-aware completion check.
4. **Dirty-column filtering**: deferred; ship restore-cost and dirty-ratio instrumentation
   in the isolation summary, revisit past ~500 ms observed restores.
5. **Distribution**: no contract assembly; the contract is the documented event name plus
   payload, mods reference only VintagestoryAPI; listener lives in the mod (internals
   needed) or its test fixture mod (public API suffices).

Staging, three PRs, each shippable alone:

- **Stage 3a: cooperation events.** Push the two events from `WorldSnapshot` (the restored
  push placed where step 5 of the question 3 sequence will sit), add `ModHookFailed` and
  its `Describe` phrase, wiki page with the payload contract and a listener snippet,
  isolation-summary instrumentation (restore cost, dirty ratio). Lands first: it already
  fixes registry desync for dimension-0-only consumers, independent of mini-dimensions.
  Acceptance: an `Atlas.Engine.Tests` mod fixture that mutates registry-style state,
  plus a throwing-listener test asserting the degrade reason and strict failure.
- **Stage 3b: mini-dimension columns.** The capture/unload/reload/completion changes of
  question 3; delete the `MiniDimensionChunksLoaded` guard and reason; stage 0 style
  equality test covering a polluted mini-dimension (blocks, block entities, chunk-stored
  entities), a scenario-created dimension whose columns must vanish on rollback, and a
  boot-pregenerated dimension that must not degrade anything. Re-measure restore cost with
  mini-dimension columns in the snapshot and record it on issue #48.
- **Stage 3c: Manifold acceptance (dogfooding, in the Manifold repo).** Manifold implements
  the restored-listener; the `rollback-stage3-candidate` classes (EntityTransit,
  BlockTransit, Ephemeral, Lifecycle) flip to `RollbackWorld = true` with
  `StrictIsolation = true`, and the fixture regains boot-time pregeneration. The issue #48
  acceptance bar is green when that suite passes with zero degrades.

## Open questions

- **Client-side mirrors.** Manifold's registry events broadcast add/remove packets to
  connected clients, and its resync must decide whether a rollback-driven rebuild
  re-broadcasts (probably yes, as a fresh manifest snapshot, the same packet sent on player
  join). Headless test players tolerate anything today; this becomes real if Atlas ever
  grows a client-side harness. The contract doc should carry a short "and your clients?"
  note for mods with mirrors.
- **A quiesce-phase event** (`atlas:rollback:suspending`, before the unload pass) was
  considered and deferred: restore runs entirely on the game thread, so game-thread-driven
  mod work (Manifold's streaming driver included) cannot interleave mid-restore. A mod
  running its own off-thread world access would need it; no known consumer does. Add it
  when one exists, the naming space allows it.
- **Capture-point hook.** The base spec's open question (snapshot after a fixture seeds the
  world, via an explicit `IWorldSession` capture call) is orthogonal and still open; the
  `captured` event slots into whatever capture point that design chooses.
- **Event payload growth.** Should `restored` carry the list of columns about to reload,
  so a mod can pre-warm per-column state? No consumer needs it; the `version` field keeps
  the door open without committing now.
- **Engine drift canary.** Stage 3b widens the reflected/replicated engine surface to
  `ServerSystemUnloadChunks.TryUnloadChunk` (public static, but an implementation detail in
  spirit) and the `dim * 1024` index convention. The stage 0 equality test doubles as the
  1.22.x to 1.23 canary, same policy as stage 1; budget a re-audit on each minor game bump.
