# World snapshot/rollback for faster per-scenario isolation

Date: 2026-07-06
Status: staged implementation (stage 1 shipped in 0.6.0, stage 2 shipped, stage 3 open)
Tracks: issue #2 "future: world snapshot/rollback for faster isolation"
Game version probed: Vintage Story 1.22.3 (net10.0), decompiled `VintagestoryLib.dll` /
`VintagestoryAPI.dll` (ILSpy 10.1)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[feasibility spike](../feasibility-spike.md)

## Problem

Today the only per-scenario isolation lever is `[AtlasScenario(FreshWorld = true)]`, which
`HostRegistry.RecycleAsync` implements as a full host recycle: dispose the embedded
`ServerMain`, join the game thread, boot a new server into a new scratch data path. That is
correct (fresh everything) but costs a full boot, 10-20 s per use: asset loading, mod
compilation and load, savegame open, spawn-chunk generation. A class with five polluting
scenarios pays a minute of wall clock for isolation alone.

The goal of this design: an opt-in mechanism that restores the world to its post-boot state
between scenarios without rebooting the server, at a small fraction of the cost. The key
question is what the cheapest *correct* rollback unit is, so most of this document is an
audit of what state exists, where it lives, and which engine paths can already tear it down
and rebuild it.

## Engine findings (decompile evidence)

All types below are from `VintagestoryLib.dll` 1.22.3 unless noted. Line references are to
the ILSpy output and will drift across game versions; type and member names are the stable
citation.

### Boot is one-way: the run-phase ladder cannot be re-entered

`ServerMain.Launch()` walks `EnterRunPhase` through `Start`, `Initialization`,
`Configuration`, `LoadAssets`, `AssetsFinalize`, `LoadGamePre`, `GameReady`, `WorldReady`,
`RunGame`. The savegame database is opened once, inside the `Configuration` phase:
`ServerSystemLoadAndSaveGame.OnBeginConfiguration()` creates
`chunkthread.gameDatabase = new GameDatabase(...)`, calls `ProbeOpenConnection` on
`server.GetSaveFilepath()` and then `LoadSaveGame()`, which populates
`server.SaveGameData`, transfers player data, and calls `server.WorldMap.Init(...)`. Mods
observe the result exactly once (`TriggerSaveGameCreated` / `TriggerSaveGameLoaded` in
`OnBeginModsAndConfigReady`), then `OnWorldgenStartup` blocking-loads the spawn area.

There is no engine path that runs any of this again on a live server. `EnterRunPhase` only
moves forward; each `ServerSystem` keys its lifecycle off the transition. Re-running the
world-load portion in place would mean hand-orchestrating the ~32 systems `Launch()` wires
up (the `Systems` array) plus every mod's savegame hooks, out of order with the phases they
were designed around.

### The save cycle: everything funnels into one SQLite file through `GameDatabase`

`Vintagestory.Common.GameDatabase` is a public wrapper over one SQLite connection
(`SQLiteDbConnectionv2`, WAL journal when `ServerConfig.CorruptionProtection` is on, which
is the default). Its public surface is exactly the granularity a snapshot needs:

- chunks: `GetChunk(x,y,z,dim)`, `SetChunks(IEnumerable<DbChunk>)`,
  `DeleteChunks(coords)`, `GetAllChunks()` (yields `DbChunk { ChunkPos Position, byte[]
  Data }` for every row)
- map chunks / map regions: same trio each
- savegame blob: `GetSaveGame()` / `StoreSaveGame(SaveGame, ...)` (protobuf of the
  `SaveGame` class)
- player data: `GetPlayerData(uid)` / `SetPlayerData(uid, data)`

Writes are serialized internally (`lock (transactionLock)` around a transaction in
`SQLiteDbConnectionv2.SetChunks` and friends), so the connection is callable from any
thread; the engine itself calls it from the chunk thread while the main thread runs.

A world save (`ServerSystemLoadAndSaveGame.SaveGameWorld`, reached via
`EventManager.TriggerGameWorldBeingSaved()`) writes, in order: per-player
`ServerWorldPlayerData` via `SetPlayerData`, dirty map regions, dirty map chunks, dirty
loaded chunks (`SaveAllDirtyLoadedChunks`), dirty generating chunks, then the `SaveGame`
blob via `StoreSaveGame`. In the common `saveLater: true` path the chunk and savegame
writes happen off-thread: `chunkthread.runOffThreadSaveNow` is set and
`OnSeparateThreadTick` (chunk thread) performs the writes under `savingLock`, clearing the
flag when done. Autosave (`ServerSystemAutoSaveGame`) fires this every
`MagicNum.ServerAutoSave` seconds (default 300, a public static loaded from
`servermagicnumbers.json` in the data path); `/autosavenow` triggers the same path on
demand.

Consequence for rollback: the database is the single persistent representation of world
state, and after a forced save it is a *complete* representation of everything the engine
itself knows how to restore.

### `SaveGame`: the non-chunk world state is one protobuf blob

`SaveGame` (public, `[ProtoContract]`) carries the global mutable state: `TotalGameSeconds`
and `TotalGameSecondsStart` (calendar), `LastEntityId`, `LastHerdId`,
`MiniDimensionsCreated`, `ModData` (`ConcurrentDictionary<string, byte[]>`, the
`api.WorldManager.SaveGame.StoreData` store), `DefaultSpawn`, `LandClaims`,
`WorldConfigBytes`. `ServerMain.SaveGameData` is internal, but
`api.WorldManager.SaveGame` returns the same instance as `ISaveGame`, and the concrete
`SaveGame` type is public, so a cast reaches every field without reflection.

The calendar is reconstructible: `ServerSystemCalendar` builds `GameCalendar` from the
savegame at boot and `GameCalendar.SetTotalSeconds(long totalSecondsNow, long
totalSecondsStart)` is public (the engine itself uses it to apply a loaded savegame), so
rolling time back is a supported operation, not a hack.

### Chunk unload: the engine already knows how to discard live world state

Two unload paths exist, with one critical difference:

1. The background path (`ServerSystemUnloadChunks.TryUnloadChunk`): despawns the chunk's
   entities (`server.DespawnEntity`, reason `Unload`, explicitly skipping `EntityPlayer`),
   calls `OnBlockUnloaded()` on every block entity, removes the chunk from
   `server.loadedChunks`, and *queues dirty chunks for saving* (`dirtyUnloadedChunks`,
   flushed to the database off-thread by `SaveDirtyUnloadedChunks`).
2. The public API path (`WorldAPI.UnloadChunkColumn(int chunkX, int chunkZ)`, the
   implementation behind `IWorldManagerAPI.UnloadChunkColumn`): same despawn +
   `OnBlockUnloaded` + removal + client unload packets + `TriggerChunkColumnUnloaded` mod
   event, but it never touches `DirtyForSaving` and never writes to the database; the
   chunk object is simply `Dispose()`d.

Path 2 is exactly "discard in-memory world state without persisting it": the primitive a
rollback needs. It is dimension-0 only (its Y loop indexes `ChunkIndex3D(chunkX, i,
chunkZ)` for `i < ChunkMapSizeY`), which matters for mods that create mini-dimensions
(see open questions).

### Chunk load: a full, mod-visible re-initialization from database bytes

`IWorldManagerAPI.LoadChunkColumnPriority` enqueues a `ChunkColumnLoadRequest`; the chunk
thread (`ServerSystemSupplyChunks.loadOrGenerateChunkColumn_OnChunkThread`) finds the
column already generated (`CurrentIncompletePass == Done` on the map chunk) and takes
`TryLoadChunkColumn`, which reads `gameDatabase.GetChunk(...)` and inflates via
`ServerChunk.FromBytes`. Completion is marshalled to the main thread
(`server.EnqueueMainThreadTask(() => { mainThreadLoadChunkColumn(chunkRequest); ... })`),
which:

- inserts the chunks into `server.loadedChunks`,
- re-registers every serialized entity through `server.LoadEntity` (entities are stored
  in the chunk blob when `entity.StoreWithChunk`, see
  `ServerChunk.GatherEntitiesToSerialize`; `EntityPlayer` is not),
- re-creates and re-initializes every block entity (`value.Initialize(server.api)`),
- fires `TriggerChunkColumnLoaded` and `TriggerChunkDirty(NewlyLoaded)`,
- removes the completed request from `requestedChunkColumns`, so no stale in-memory
  chunk reference survives for the next request of the same column.

So unload-then-reload of a column is a genuine round trip through serialized state: block
data, block entities, entities, chunk moddata all come back exactly as the database has
them, and mods that follow the standard chunk/entity lifecycle events see a coherent
unload/load sequence. `ServerMain.IsChunkColumnFullyLoaded(x, z)` (public) reports
completion, and the finalize step runs inside `server.Process()`, which the Atlas game
thread already pumps.

The world is small in tests: spawn preload is `MagicNum.SpawnChunksWidth = 7`, i.e. a 7x7
column area, and the default 256-block-high superflat world is 8 chunks per column, so a
baseline Atlas world is roughly 50 columns / 400 chunks, most of them near-empty and
cheap to serialize.

### What a reboot pays that a rollback would not

`ServerMain.Dispose()` disposes process-wide statics: `TyronThreadPool.Inst.Dispose()`,
`ClassRegistry = null`, `Logger = null`. `ServerMain.Stop()` waits up to 60 s for server
threads and does a final save. A fresh boot then re-runs `Lang.PreLoad`, full
`AssetManager` loading, `FinalizeAssets`, mod loading/compilation and worldgen init. None
of that work is world-state; all of it is repeated on every `FreshWorld = true`. This is
the structural reason a reboot cannot get much cheaper (the engine tears down its own
statics; see also the issue #8 shutdown NRE around `ServerMain.Logger`), and the reason a
rollback that keeps `ServerMain` alive skips the entire bill.

### Players are host-scoped and survive every path short of a reboot

Both unload paths explicitly skip `EntityPlayer`, and players are not serialized into
chunk blobs (`StoreWithChunk` is false for them). Their persistent state
(`ServerWorldPlayerData`: game mode, spawn, `ModData`, inventories via the entity) is
saved separately through `SetPlayerData` during a world save. Test players joined through
`WorldSession.JoinPlayer` hold dummy connections owned by the host
(`_joinedPlayerNames` in `ServerHost` is already host-scoped for exactly this reason) and
receive the standard unload packets (`Packet_UnloadServerChunk`) followed by fresh chunk
sends; nothing reads the dummy buffers, so this is inert. Rollback therefore does not
disconnect players, and conversely does not roll their entity state back (position,
inventory, hunger); that is a documented boundary, not an accident (see stage 2).

## Options compared

| | Option | Feasibility | Cost per isolation | Correctness | Maintenance |
|---|---|---|---|---|---|
| (a) | Full host recycle (status quo) | Proven | 10-20 s | Total (fresh statics, mods, world) | None (exists) |
| (b) | In-process world reload (keep `ServerMain`, reopen savegame) | Not feasible | n/a | n/a | n/a |
| (c) | Database snapshot + chunk column recycle | Feasible on public API + 2 internals | ~0.3-2 s (to be measured) | World state complete; mod in-memory state and player entities excluded | Low-moderate |
| (d) | Dirty-state tracking and undo | Not feasible as a guarantee | n/a | n/a | n/a |

### (a) Full host recycle: keep as the correctness baseline

Correct by construction and already battle-tested, including its known engine warts
(issue #8). Every other option is an optimization that must be allowed to fall back to
this. Nothing to build.

### (b) Save-file copy + in-process world reload: rejected

The idea: keep `ServerMain` and its loaded assets, close the game database, copy a
pristine `.vcdbs` over it, and re-run the world-load half of boot. The decompile shows
this half does not exist as a callable unit: database open and `LoadSaveGame` live inside
`OnBeginConfiguration`, world map init inside `AfterSaveGameLoaded`, mod notification
inside `OnBeginModsAndConfigReady`, spawn loading inside `OnWorldgenStartup`, all keyed to
one-way `EnterRunPhase` transitions across ~32 systems (plus every mod's assumption that
`SaveGameLoaded` fires once per process). Re-implementing that ordering inside Atlas would
be a fork of `Launch()` in all but name, revalidated on every game patch. Rejected on
maintenance cost, independent of whether it could be made to work once.

(A file-level variant, copying the `.vcdbs` and rebooting, is just option (a) plus
`WorldOptions.SaveFile`, which Atlas already supports; it saves worldgen but still pays
assets and mod loading.)

### (c) Database snapshot + chunk column recycle: recommended

Use the engine's own persistence round trip as the rollback mechanism, entirely in
process:

Snapshot (once per class, after boot, before the first scenario):

1. On the game thread, force a full save so the database matches memory: trigger the
   `/autosavenow` path (or `EventManager.TriggerGameWorldBeingSaved()` directly), then
   wait for `chunkThread.runOffThreadSaveNow` to clear (the off-thread half of
   `SaveGameWorld`).
2. Read the entire database into memory through the open connection:
   `GetAllChunks()`, `GetAllMapChunks()`, `GetAllMapRegions()`, the savegame blob, and
   `GetPlayerData` for any already-joined test players. Record the set of loaded column
   indices (`ServerMain.LoadedChunkIndices` / `LoadedMapChunkIndices`, both public) and
   the calendar pair (`TotalGameSeconds`, `TotalGameSecondsStart`). A baseline Atlas
   world is a few MB; keeping the blobs in memory avoids any file-level interaction with
   the WAL journal.

Rollback (between scenarios):

1. On the game thread, with autosave disabled for the host's lifetime (set the public
   static `MagicNum.ServerAutoSave = 0` at boot, or seed `servermagicnumbers.json`),
   verify no off-thread save is in flight.
2. Unload every loaded chunk column via the `WorldAPI.UnloadChunkColumn` semantics: this
   despawns scenario-spawned entities, unloads block entities, fires the mod unload
   events, and *discards* in-memory changes without persisting them.
3. Restore the database through the open `GameDatabase`: enumerate current chunk
   positions, `DeleteChunks` for positions absent from the snapshot (columns the scenario
   caused to be generated), then `SetChunks` / `SetMapChunks` / `SetMapRegions` /
   `StoreSaveGame` / `SetPlayerData` with the snapshot blobs. All of these are the same
   calls the engine's own save uses, serialized by the connection's internal lock.
4. Restore in-memory globals on the live `SaveGame` instance (cast from
   `api.WorldManager.SaveGame`): `ModData` contents, `LastEntityId`, `LastHerdId`,
   `MiniDimensionsCreated`, `DefaultSpawn`; roll the clock back with
   `GameCalendar.SetTotalSeconds(snapshot.TotalGameSeconds, snapshot.TotalGameSecondsStart)`
   (cast from `api.World.Calendar`).
5. Re-request the snapshot's column set (`LoadChunkColumnPriority`, `keepLoaded` for
   columns that were force-loaded) and pump ticks until
   `IsChunkColumnFullyLoaded` holds for all of them; the reload path re-initializes block
   entities and re-registers entities as described above. Columns near a connected test
   player are re-requested by the engine itself; the explicit pass covers the rest.

Engine access needed beyond the public API: `ServerMain.chunkThread` (internal field) and
`ChunkServerThread.gameDatabase` (internal field), reached once via reflection at host
boot and validated with a fail-fast check (if 1.23 renames either, the feature degrades
to `FreshWorld` with a clear message instead of corrupting a run). Everything else in the
plan is public surface: `IWorldManagerAPI`, `GameDatabase`, `SaveGame`, `GameCalendar`,
`MagicNum`, `LoadedChunkIndices`.

Cost estimate (to be validated by the stage 0 spike, not promised): unloading ~50 columns
is a main-thread loop; restoring a few hundred small blobs is one SQLite transaction each
way; reloading is dominated by chunk deserialization plus a handful of pumped ticks.
Expected order: hundreds of milliseconds, against 10-20 s for a reboot, so roughly a
10-40x improvement where it applies.

Correctness boundary, stated honestly: this rolls back *world* state (blocks, block
entities, chunk-stored entities, chunk moddata, savegame data, calendar). It does not
roll back:

- mod in-memory state not tied to chunk/entity lifecycle: `ModSystem` fields, statics,
  caches built at `SaveGameLoaded`. Mods that key their state to
  `ChunkColumnUnloaded` / `ChunkColumnLoaded` / entity despawn events (the pattern the
  engine already forces on any mod that survives normal chunk churn) come out clean;
  mods with global accumulators do not, and no in-process design can fix that without
  their cooperation.
- player entities: connected test players keep position, inventory and stats across a
  rollback (exactly as they already keep their connection across scenarios today).
- engine oddities: queued block ticks referencing unloaded positions
  (`ServerSystemBlockSimulation.queuedTicks`; the engine tolerates stale entries, and
  `/tickqueue clear` exists as belt and suspenders), relight queues, and
  `WildcardUtil`-style caches (harmless, content-addressed).

Because of that boundary, rollback must be opt-in and `FreshWorld = true` must remain
available and unchanged; rollback failure (validation check, timeout waiting for reload)
falls back to a host recycle rather than failing the scenario.

### (d) Dirty-state tracking and undo: rejected as a guarantee, kept as an optimization

Tracking what a scenario touched (via the Atlas action surface) and undoing it cannot be
correct in general: scenarios hold the raw `ICoreServerAPI` escape hatch, mods mutate
state on their own ticks, and the engine keeps no journal to replay backwards. What the
engine does keep is per-chunk `DirtyForSaving`, which is a cheap, reliable "this chunk
changed" signal. That makes a targeted variant of (c) possible later: only recycle
columns that are dirty or whose entity set changed, skipping the (typically many)
untouched columns. This is an optimization inside option (c)'s design, not a separate
mechanism, and is deliberately deferred until measurements say the full recycle is too
slow.

## Recommendation and staged plan

Adopt option (c) as an opt-in isolation mode, keeping (a) as both the default fallback
and the semantic reference.

Proposed authoring surface (final naming in the implementation PR):

```csharp
[AtlasScenario(Isolation = ScenarioIsolation.Rollback)]   // per scenario
[AtlasWorld(Seed = 424242, Isolation = ScenarioIsolation.Rollback)] // class default
```

`FreshWorld = true` stays as the spelled-out strongest form (`ScenarioIsolation.FreshWorld`).

Stage 0, spike and measurement (engine test, no public surface):
- In `Atlas.Engine.Tests`, script the full loop against a live host: force save,
  snapshot, pollute the world (set blocks, spawn entities, advance time, generate a new
  column), rollback, assert block-for-block and entity-for-entity equality of the spawn
  area against a control host that never ran the pollution.
- Measure wall-clock cost on the default superflat world and on a `SaveFile` world.
  Success gate: >= 5x faster than `RecycleAsync` and zero diffs; otherwise stop and keep
  the issue open with the numbers attached.

Stage 1, minimal shipping form:
- `WorldSnapshot` in `Atlas.Internal.Hosting`: `Capture` (post-boot, lazy on first
  rollback-requesting scenario) and `Restore`, both executed via `RunOnGameThreadAsync`.
- Reflection shim for `chunkThread` / `gameDatabase` with boot-time validation and
  `FreshWorld` fallback.
- Scope guards, fail closed: dimension-0 columns only, no joined test players at capture
  time; a scenario that violates the guard gets a host recycle plus a log line saying
  why. Autosave disabled for hosts that will snapshot.
- xUnit adapter: `ScenarioIsolation` plumbed through `AtlasTestCase` alongside the
  existing `FreshWorld` bit; rollback runs before the scenario body, same place
  `RecycleAsync` is invoked today.

Stage 2, players:
- Allow captures with joined test players: include their `SetPlayerData` blobs in the
  snapshot and, on rollback, reset the live `EntityPlayer` best-effort (teleport to
  spawn, clear inventories via the inventory API, restore game mode from the snapshot
  world data). Document that fine-grained player entity state (stats buffs, temporal
  stability) is only reset where the engine exposes a setter.
- `_joinedPlayerNames` semantics unchanged: players persist, names stay claimed.

### Stage 2 status (shipped 2026-07-11, issue #47)

Implemented as designed, with two upgrades over the sketch above and one policy decision
the sketch left open:

- The live reset is exact, not best-effort. A capture records, per joined player: the
  `GetPlayerData` blob the forced save just wrote (restored verbatim into the database, and
  the source of the world-player-data scalars and per-player `ModData`), plus the live
  pieces re-serialized through the same public primitives the engine's own save uses
  (`ToTreeAttributes` per `InventoryBasePlayer`, watched attributes with `Entity.Stats`
  flushed in first, `EntityPos.ToBytes`). The restore mutates the EXISTING inventory
  instances and attribute trees in place: watched/plain attributes are merged key-by-key
  (removing post-capture keys, recursing into shared sub-trees) instead of `FromBytes`,
  because behaviors cache sub-tree references at initialization (e.g.
  `EntityBehaviorHunger.hungerTree`) and a wholesale replacement would leave them writing
  to detached trees. So position, health, saturation, custom watched trees, inventories
  and per-player moddata all return to their captured values on the live, still-connected
  player.
- Players joined AFTER the capture are removed by the restore, so the world returns
  exactly to its captured population: a normal `DisconnectPlayer` teardown on the game
  thread, plus purging of `WorldDataByUID`/`PlayersByUid` and the playerdata row
  (`SetPlayerData(uid, null)` deletes it), so the name can rejoin as a brand-new player.
  The `KickedPlayerCleanup` armed at join releases the Atlas-side claims (joined name, TCP
  slot) a few ticks later; the host waits for that release before handing the world back,
  so an immediate rejoin cannot race the duplicate-name guard. A captured player that LEFT
  after the capture (kicked by the mod under test) is not resurrected; its cached world
  data is purged so a rejoin under the same identity loads the restored blob, i.e. the
  captured baseline.
- Restore ordering (players must not observe half-restored chunks): remove post-capture
  players first, while the world is fully live; restore the database BEFORE the in-memory
  unload, because with connected players the engine re-requests player-adjacent columns as
  soon as ticks pump, and any such load must already read snapshot bytes; then unload
  everything, restore the in-memory globals and reset the live players, all in the same
  game-thread turn with no tick pumped in between; only then request the column reload and
  pump. No tick ever observes restored players in a polluted world (or the reverse), and
  the reload's player-adjacent requests already target the captured positions, inside the
  snapshot's column set.
- Guards lifted: `WorldSnapshot.CaptureAsync` no longer refuses joined players and
  `ServerHost.TryRollbackWorldAsync` no longer throws the players setup error, so
  `RollbackWorld = true` classes may join players freely. The
  `RollbackDegradeReason.PlayersJoined` enum member is retained (recorded summaries, TRX
  output and logs keep their meaning; remaining members keep their values) but is no
  longer produced.
- Out of scope, documented boundary: a player's animation/interaction state (open GUIs,
  controls: test players are headless and have none), and the host-scoped
  `ServerPlayerData` (privileges, role, playerdata.json), which is not world state.
  Streaming scenarios additionally wait on stage 3 (mini-dimensions).

Stage 3, dimensions and dirty-only optimization:
- Mini-dimension columns (chunks keyed with a dimension offset in `loadedChunks`, created
  via `CreateChunkColumnForDimension`): replicate `UnloadChunkColumn` for dim > 0 indices
  and restore `MiniDimensionsCreated`; needed before Manifold's dimension scenarios can
  use rollback.
- If stage 0/1 numbers justify it: dirty-column filtering per option (d).

## Open questions

- Mod cooperation contract: is "your state must follow chunk/entity lifecycle events"
  acceptable to the first consumers (Manifold, Chart)? If not, the bridge could
  broadcast an explicit Atlas rollback notification mods can subscribe to; that is API
  surface and deserves its own issue once a real mod hits the boundary.
- Entities with `StoreWithChunk == false` in the baseline world: none are expected in a
  superflat creative world, but a `SaveFile` fixture could contain systems that spawn
  them at load; the stage 0 equality assertion is the detector.
- Snapshot timing vs class fixtures that mutate the world intentionally in a constructor
  or first scenario: is post-boot always the right capture point, or does "capture now"
  deserve a public hook on `IWorldSession` (e.g. seed the world once, then snapshot,
  then run scenarios against the seeded baseline)? Leaning toward the hook; it is cheap
  once `WorldSnapshot` exists.
- Concurrency edges during restore: the design assumes no database writer is active
  (autosave off, `runOffThreadSaveNow` drained, unload path 2 never writes). The chunk
  thread's `peekMode` and worldgen threads are quiescent for fully generated columns,
  but the spike should run with the SQLite busy timeout logged to catch any writer we
  missed.
- Version drift: the two reflected internals aside, everything rides public API, but
  path 2's "unload without saving" behavior is an implementation detail of
  `WorldAPI.UnloadChunkColumn`, not a documented contract. The stage 0 test doubles as
  the 1.22.x -> 1.23 canary; budget a re-audit of `ServerSystemUnloadChunks` /
  `WorldAPI` on each minor game bump.
