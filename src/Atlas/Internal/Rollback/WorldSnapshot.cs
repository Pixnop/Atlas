using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Atlas.Api;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Atlas.Internal.Rollback;

/// <summary>World snapshot/rollback (see docs/specs/2026-07-06-world-snapshot-rollback.md,
/// option (c)): captures the whole world database through the engine's own open
/// <see cref="GameDatabase"/> after a forced save, and rolls the live world back to that
/// snapshot by recycling every loaded chunk column through the engine's public unload (discard)
/// and load (reload from database) paths. Capture once, restore many.</summary>
/// <remarks><para>Every call runs on the game thread via the host's scheduler. The only engine
/// access beyond the public surface is the boot-validated reflection in <see cref="Create"/>
/// over the two internals the spec names: <c>ServerMain.chunkThread</c> and
/// <c>ChunkServerThread.gameDatabase</c>.</para>
/// <para>Players (stage 2, per the spec): a capture records, for every joined test player, the
/// playerdata blob the forced save just wrote plus the live pieces a reset needs (see
/// <see cref="PlayerRollbackState"/>). A restore resets those players in place (entity state,
/// inventories, per-player moddata) and DISCONNECTS players that joined after the capture, so
/// the world returns exactly to its captured population; their engine-side identity caches and
/// playerdata rows are purged too, so the same name can rejoin from scratch. A captured player
/// that left after the capture (e.g. kicked by the mod under test) is not resurrected: only its
/// persistent blob is restored, so a scenario that rejoins the name gets the captured baseline.</para>
/// <para>Restore ordering, chosen deliberately: post-capture players are removed first (their
/// despawn touches the still-live world); the database is restored BEFORE the in-memory unload,
/// because with connected players the engine re-requests player-adjacent columns as soon as
/// ticks pump again, and any such load must already read snapshot bytes; live players are then
/// reset in the same game-thread turn as the unload and the global restore, before a single
/// tick is pumped, so no tick ever observes restored players in a polluted world (or polluted
/// players in a restored world) and the reload's player-adjacent chunk requests already target
/// the captured positions, which lie inside the snapshot's column set.</para>
/// <para>Correctness boundary (per the spec): a restore brings back world state (blocks, block
/// entities, chunk-stored entities, chunk moddata, the savegame blob including
/// <c>SaveGame.ModData</c>, the calendar) and joined-player state as above. It does NOT
/// restore: mod in-memory state that is not tied to chunk/entity lifecycle events (ModSystem
/// fields, statics, caches); in-memory <c>ServerMapChunk</c> state, because the engine's
/// <c>GetOrCreateMapChunk</c> prefers the live in-memory instance over the restored database
/// blob, so map-chunk-level data (height maps, map moddata) mutated by a scenario survives the
/// rollback; and, for players, animation/interaction state and the host-scoped
/// <c>ServerPlayerData</c> (privileges, role), see <see cref="PlayerRollbackState"/>.</para></remarks>
internal sealed class WorldSnapshot : IWorldSnapshot
{
    private readonly ICoreServerAPI _api;
    private readonly ServerMain _server;
    private readonly TickSource _ticks;
    private readonly ChunkServerThread _chunkThread;
    private readonly GameDatabase _database;

    private List<DbChunk>? _chunks;
    private HashSet<ulong>? _chunkIndices;
    private List<DbChunk>? _mapChunks;
    private HashSet<ulong>? _mapChunkIndices;
    private List<DbChunk>? _mapRegions;
    private HashSet<ulong>? _mapRegionIndices;
    private SaveGame? _saveGame;
    private List<(int X, int Z)>? _columns;
    private List<PlayerRollbackState>? _players;
    private HashSet<string>? _playerUids;

    /// <summary>Initializes a new instance of the <see cref="WorldSnapshot"/> class.
    /// Use <see cref="Create"/>; the constructor only stores the already-validated references.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="server">The live embedded server.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <param name="chunkThread">The engine's chunk thread (reflected).</param>
    /// <param name="database">The engine's open game database (reflected).</param>
    private WorldSnapshot(
        ICoreServerAPI api,
        ServerMain server,
        TickSource ticks,
        ChunkServerThread chunkThread,
        GameDatabase database)
    {
        _api = api;
        _server = server;
        _ticks = ticks;
        _chunkThread = chunkThread;
        _database = database;
    }

    /// <summary>Gets a value indicating whether <see cref="CaptureAsync"/> has completed.</summary>
    public bool IsCaptured => _chunks != null;

    /// <summary>Gets the number of chunk blobs captured by <see cref="CaptureAsync"/>.</summary>
    public int SnapshotChunkCount => _chunks?.Count ?? 0;

    /// <summary>Gets the loaded chunk columns recorded by <see cref="CaptureAsync"/>.</summary>
    public IReadOnlyList<(int X, int Z)> SnapshotColumns => _columns ?? [];

    /// <summary>Gets the number of joined test players captured by <see cref="CaptureAsync"/>.</summary>
    public int SnapshotPlayerCount => _players?.Count ?? 0;

    /// <summary>Resolves the two engine internals the rollback needs and fails fast with a clear
    /// message if the game version renamed either (the spec's boot-validation requirement).</summary>
    /// <param name="api">The live server API; its <c>World</c> is the embedded <see cref="ServerMain"/>.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <returns>A validated snapshot instance, not yet captured.</returns>
    /// <exception cref="RollbackUnsupportedException">Thrown, with
    /// <see cref="RollbackDegradeReason.EngineDrift"/>, when a reflected internal is missing or
    /// null: the engine layout drifted and rollback cannot work on this game version.</exception>
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "The design spec's boot-validated reflection: ServerMain.chunkThread and ChunkServerThread.gameDatabase are the only two engine internals rollback needs, and both throws name the game version so drift fails fast instead of corrupting worlds.")]
    public static WorldSnapshot Create(ICoreServerAPI api, TickSource ticks)
    {
        var server = (ServerMain)api.World;

        FieldInfo chunkThreadField = typeof(ServerMain).GetField(
            "chunkThread", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new RollbackUnsupportedException(
                "World rollback: internal field 'ServerMain.chunkThread' not found on game " +
                $"version {GameVersion.ShortGameVersion}; the engine layout changed, rollback " +
                "is not available.",
                RollbackDegradeReason.EngineDrift);
        var chunkThread = (ChunkServerThread?)chunkThreadField.GetValue(server)
            ?? throw new RollbackUnsupportedException(
                "World rollback: 'ServerMain.chunkThread' is null; the server is not fully booted.",
                RollbackDegradeReason.EngineDrift);

        FieldInfo databaseField = typeof(ChunkServerThread).GetField(
            "gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new RollbackUnsupportedException(
                "World rollback: internal field 'ChunkServerThread.gameDatabase' not found on " +
                $"game version {GameVersion.ShortGameVersion}; the engine layout changed, " +
                "rollback is not available.",
                RollbackDegradeReason.EngineDrift);
        var database = (GameDatabase?)databaseField.GetValue(chunkThread)
            ?? throw new RollbackUnsupportedException(
                "World rollback: 'ChunkServerThread.gameDatabase' is null; the savegame is not open.",
                RollbackDegradeReason.EngineDrift);

        return new WorldSnapshot(api, server, ticks, chunkThread, database);
    }

    /// <summary>Snapshots the world: forces one full save, waits for its off-thread half to
    /// settle, then reads every blob back through the open database, including the playerdata
    /// blob of every joined test player alongside the live pieces its reset needs.</summary>
    /// <returns>A task that completes when the snapshot is in memory.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the engine's save machinery
    /// misbehaves.</exception>
    /// <remarks>Also quiets the two background database writers for the host's lifetime, per the
    /// spec's concurrency notes: autosave via the public <see cref="MagicNum.ServerAutoSave"/>
    /// knob, and the background chunk unloader (which persists dirty chunks it evicts roughly
    /// every 3 seconds even with no players connected, unlike the discard path rollback uses)
    /// via the engine's own /chunk unload toggle.</remarks>
    [SuppressMessage(
        "Critical Code Smell",
        "S2696:Instance members should not write to static fields",
        Justification = "MagicNum.ServerAutoSave is the engine's process-wide autosave knob and the public API the spec prescribes; one live server per process is the invariant that makes writing it safe.")]
    public async Task CaptureAsync()
    {
        // Quiet the background writers: no timed autosave, no dirty-persisting background unloads.
        MagicNum.ServerAutoSave = 0;
        string unloadMessage = await ExecuteConsoleAsync("/chunk unload false").ConfigureAwait(true);
        if (!unloadMessage.Contains("off", StringComparison.Ordinal))
        {
            throw new AtlasSetupException(
                $"World rollback: could not pause background chunk unloading: '{unloadMessage}'.");
        }

        // Force one save so the database is a complete image of the live world, and wait for the
        // off-thread half (dirty chunks, map chunks, savegame blob) to be written out.
        await WaitForSaveIdleAsync("before the forced save").ConfigureAwait(true);
        string saveMessage = await ExecuteConsoleAsync("/autosavenow").ConfigureAwait(true);
        if (!saveMessage.Contains("Autosave completed", StringComparison.Ordinal))
        {
            throw new AtlasSetupException(
                $"World rollback: the engine skipped the forced save: '{saveMessage}'.");
        }

        await WaitForSaveIdleAsync("after the forced save").ConfigureAwait(true);

        // Read the complete database into memory through the already-open connection. Assigned
        // as one block at the end, so a mid-capture failure never leaves a half-built snapshot
        // behind (IsCaptured keys off _chunks, the last field assigned).
        List<DbChunk> chunks = CloneTable(_database.GetAllChunks());
        List<DbChunk> mapChunks = CloneTable(_database.GetAllMapChunks());
        List<DbChunk> mapRegions = CloneTable(_database.GetAllMapRegions());
        SaveGame saveGame = _database.GetSaveGame();
        List<(int X, int Z)> columns = LoadedColumns();
        List<PlayerRollbackState> players = CapturePlayers();

        _players = players;
        _playerUids = [.. players.Select(player => player.PlayerUid)];
        _chunkIndices = IndexSet(chunks);
        _mapChunks = mapChunks;
        _mapChunkIndices = IndexSet(mapChunks);
        _mapRegions = mapRegions;
        _mapRegionIndices = IndexSet(mapRegions);
        _saveGame = saveGame;
        _columns = columns;
        _chunks = chunks;
    }

    /// <summary>Rolls the live world back to the snapshot: removes players that joined after the
    /// capture, restores the database blobs (playerdata included), unloads every loaded chunk
    /// column through the discarding public path, restores the in-memory globals, resets the
    /// live captured players, then reloads the snapshot's column set and pumps until fully
    /// loaded. See the class remarks for why the steps run in exactly this order.</summary>
    /// <returns>A task that completes when every snapshot column is fully loaded again.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="CaptureAsync"/> has not
    /// completed yet.</exception>
    /// <exception cref="AtlasSetupException">Thrown when an engine path did not behave as the
    /// spec's audit found (e.g. chunks survive the unload pass).</exception>
    public async Task RestoreAsync()
    {
        if (_chunks == null || _chunkIndices == null || _mapChunks == null || _mapChunkIndices == null
            || _mapRegions == null || _mapRegionIndices == null || _saveGame == null || _columns == null
            || _players == null || _playerUids == null)
        {
            throw new InvalidOperationException("CaptureAsync must complete before RestoreAsync.");
        }

        await WaitForSaveIdleAsync("before rollback").ConfigureAwait(true);

        // 1. Return the world to its captured population: disconnect players that joined after
        //    the capture, while the world is still fully live (their entity despawn touches
        //    loaded chunks).
        RemovePostCapturePlayers();

        // 2. Restore the database BEFORE discarding live state: delete rows the polluted world
        //    added, write the snapshot back, chunks and playerdata alike. Doing the database
        //    first matters with connected players: the engine re-requests player-adjacent
        //    columns as soon as ticks pump again, and any such load must already read snapshot
        //    bytes. No writer can interleave: both background writers were quieted at capture,
        //    the off-thread save is drained, and the unload path below never persists.
        DeleteExtraRows();
        _database.SetChunks(_chunks);
        _database.SetMapChunks(_mapChunks);
        _database.SetMapRegions(_mapRegions);
        _database.StoreSaveGame(_saveGame);
        foreach (PlayerRollbackState player in _players)
        {
            _database.SetPlayerData(player.PlayerUid, player.DatabaseBlob);
        }

        // 3. Discard all live world state: the public UnloadChunkColumn path despawns entities
        //    (explicitly skipping player entities), unloads block entities, fires the mod unload
        //    events, and never persists anything.
        foreach ((int x, int z) in LoadedColumns())
        {
            _api.WorldManager.UnloadChunkColumn(x, z);
        }

        if (_server.LoadedChunkIndices.Length != 0)
        {
            throw new AtlasSetupException(
                $"World rollback: {_server.LoadedChunkIndices.Length} chunks still loaded " +
                "after unloading every column; unload path did not behave as the spec assumes.");
        }

        // 4. Restore in-memory globals on the live SaveGame instance and roll the clock back.
        RestoreGlobals();

        // 5. Reset the live captured players, still in the same game-thread turn (no tick has
        //    been pumped since the unload): no tick ever observes restored players in a polluted
        //    world or vice versa, and the reload below already sees them at their captured
        //    positions. A captured player that is no longer connected (kicked post-capture) is
        //    not resurrected; purging its cached world data makes a rejoin load the restored
        //    database blob, i.e. the captured baseline.
        RestoreCapturedPlayers();

        // 6. Reload the snapshot's columns from the restored database and pump until done.
        //    KeepLoaded pins them, mirroring the boot-time spawn preload's force-loaded set.
        foreach ((int x, int z) in _columns)
        {
            _api.WorldManager.LoadChunkColumnPriority(x, z, new ChunkLoadOptions { KeepLoaded = true });
        }

        List<(int X, int Z)> columns = _columns;
        await _ticks.WaitUntilAsync(
            () => columns.All(c => _server.IsChunkColumnFullyLoaded(c.X, c.Z)),
            timeoutTicks: 5000).ConfigureAwait(true);
    }

    /// <summary>Captures the rollback baseline of every fully joined test player (entity spawned,
    /// world data present). A client still mid-join is skipped, which classifies it as
    /// post-capture: the next restore removes it.</summary>
    /// <returns>The captured player states.</returns>
    private List<PlayerRollbackState> CapturePlayers()
        => [.. _server.Clients
            .Select(pair => pair.Value)
            .Where(client => client.Player?.PlayerUID != null && client.Entityplayer != null)
            .Select(client => PlayerRollbackState.Capture(client, _database))];

    /// <summary>Disconnects every client that is not part of the snapshot, and erases its
    /// engine-side traces (the cached world data, the players-by-uid entry, any playerdata row a
    /// mid-scenario save persisted), so the same name can rejoin later as a brand-new player.
    /// The disconnect runs the engine's own full teardown on the game thread; the
    /// <c>KickedPlayerCleanup</c> armed at join observes it and releases the Atlas-side claims
    /// (joined name, TCP slot) on the following ticks.</summary>
    private void RemovePostCapturePlayers()
    {
        List<ConnectedClient> postCapture = [.. _server.Clients
            .Select(pair => pair.Value)
            .Where(client => client.Player?.PlayerUID is not { } uid || !_playerUids!.Contains(uid))];
        foreach (ConnectedClient client in postCapture)
        {
            string? uid = client.Player?.PlayerUID;
            _server.DisconnectPlayer(
                client,
                othersKickmessage: null,
                hisKickMessage: "Atlas world rollback: this player joined after the world snapshot was captured.");
            if (uid != null)
            {
                _server.PlayerDataManager.WorldDataByUID.Remove(uid);
                _server.PlayersByUid.Remove(uid);
                _database.SetPlayerData(uid, null); // a null blob DELETES the row (engine contract)
            }
        }
    }

    /// <summary>Resets every captured player that is still connected (see
    /// <see cref="PlayerRollbackState.RestoreLive"/>); for captured players that left after the
    /// capture, purges the engine's cached world data so a rejoin under the same identity loads
    /// the restored database blob instead of the stale post-capture state.</summary>
    private void RestoreCapturedPlayers()
    {
        Dictionary<string, ConnectedClient> connectedByUid = _server.Clients
            .Select(pair => pair.Value)
            .Where(client => client.Player?.PlayerUID != null)
            .ToDictionary(client => client.Player.PlayerUID);
        foreach (PlayerRollbackState player in _players!)
        {
            if (connectedByUid.TryGetValue(player.PlayerUid, out ConnectedClient? client))
            {
                player.RestoreLive(client);
            }
            else
            {
                _server.PlayerDataManager.WorldDataByUID.Remove(player.PlayerUid);
                _server.PlayersByUid.Remove(player.PlayerUid);
            }
        }
    }

    /// <summary>Copies a database table into memory, cloning each blob so the snapshot cannot
    /// alias buffers the engine may reuse.</summary>
    /// <param name="rows">The table enumeration from the open database.</param>
    /// <returns>The materialized rows.</returns>
    private static List<DbChunk> CloneTable(IEnumerable<DbChunk> rows)
        => [.. rows.Select(row => new DbChunk(row.Position, (byte[])row.Data.Clone()))];

    /// <summary>Builds the position-index set of a captured table, for extra-row reconciliation.</summary>
    /// <param name="rows">The captured rows.</param>
    /// <returns>The set of position indices.</returns>
    private static HashSet<ulong> IndexSet(List<DbChunk> rows)
        => [.. rows.Select(row => row.Position.ToChunkIndex())];

    /// <summary>Deletes database rows that did not exist at snapshot time (columns the polluted
    /// world caused to be generated and saved).</summary>
    private void DeleteExtraRows()
    {
        List<ChunkPos> extraChunks =
            [.. _database.GetAllChunks().Select(c => c.Position).Where(p => !_chunkIndices!.Contains(p.ToChunkIndex()))];
        if (extraChunks.Count > 0)
        {
            _database.DeleteChunks(extraChunks);
        }

        List<ChunkPos> extraMapChunks =
            [.. _database.GetAllMapChunks().Select(c => c.Position).Where(p => !_mapChunkIndices!.Contains(p.ToChunkIndex()))];
        if (extraMapChunks.Count > 0)
        {
            _database.DeleteMapChunks(extraMapChunks);
        }

        List<ChunkPos> extraMapRegions =
            [.. _database.GetAllMapRegions().Select(c => c.Position).Where(p => !_mapRegionIndices!.Contains(p.ToChunkIndex()))];
        if (extraMapRegions.Count > 0)
        {
            _database.DeleteMapRegions(extraMapRegions);
        }
    }

    /// <summary>Restores the snapshot's global mutable state on the live <see cref="SaveGame"/>
    /// instance (the same object <c>api.WorldManager.SaveGame</c> exposes) and rolls the calendar
    /// back with the engine's own supported setter.</summary>
    private void RestoreGlobals()
    {
        SaveGame snapshot = _saveGame!;
        var live = (SaveGame)_api.WorldManager.SaveGame;
        live.LastEntityId = snapshot.LastEntityId;
        live.LastHerdId = snapshot.LastHerdId;
        live.MiniDimensionsCreated = snapshot.MiniDimensionsCreated;
        live.DefaultSpawn = snapshot.DefaultSpawn;
        live.TotalGameSeconds = snapshot.TotalGameSeconds;
        live.TotalGameSecondsStart = snapshot.TotalGameSecondsStart;
        live.ModData.Clear();
        foreach (KeyValuePair<string, byte[]> entry in snapshot.ModData)
        {
            live.ModData[entry.Key] = entry.Value;
        }

        ((GameCalendar)_api.World.Calendar).SetTotalSeconds(
            snapshot.TotalGameSeconds, snapshot.TotalGameSecondsStart);
    }

    /// <summary>Waits until no off-thread save is in flight and the engine reports itself ready
    /// to save, pumping the game thread meanwhile.</summary>
    /// <param name="stage">Human-readable stage name for the timeout message.</param>
    /// <returns>A task that completes when the save machinery is idle.</returns>
    private async Task WaitForSaveIdleAsync(string stage)
    {
        try
        {
            await _ticks.WaitUntilAsync(
                () => _server.readyToAutoSave && !_chunkThread.runOffThreadSaveNow,
                timeoutTicks: 5000).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new AtlasSetupException(
                $"World rollback: save machinery still busy {stage} " +
                $"(readyToAutoSave={_server.readyToAutoSave}, runOffThreadSaveNow={_chunkThread.runOffThreadSaveNow}).",
                ex);
        }
    }

    /// <summary>Reads the currently loaded chunk columns from the public loaded-chunk index list.</summary>
    /// <returns>The distinct loaded column coordinates, dimension 0 only.</returns>
    /// <exception cref="RollbackUnsupportedException">Thrown, with
    /// <see cref="RollbackDegradeReason.MiniDimensionChunksLoaded"/>, when a loaded chunk lives
    /// in a mini-dimension: out of scope until stage 3 (the spec defers dimensions there).</exception>
    private List<(int X, int Z)> LoadedColumns()
    {
        var columns = new HashSet<(int X, int Z)>();
        foreach (long index in _server.LoadedChunkIndices)
        {
            ChunkPos pos = _server.WorldMap.ChunkPosFromChunkIndex3D(index);
            if (pos.Dimension != 0)
            {
                throw new RollbackUnsupportedException(
                    $"World rollback: loaded chunk in dimension {pos.Dimension}; " +
                    "mini-dimensions are out of scope until stage 3 of the rollback spec.",
                    RollbackDegradeReason.MiniDimensionChunksLoaded);
            }

            columns.Add((pos.X, pos.Z));
        }

        return [.. columns];
    }

    /// <summary>Executes a chat command as the server console (mirrors the world session's
    /// command plumbing, without needing a scenario surface).</summary>
    /// <param name="command">The slash-prefixed command.</param>
    /// <returns>The command's final status message.</returns>
    private Task<string> ExecuteConsoleAsync(string command)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs
            {
                Caller = new Caller
                {
                    Type = EnumCallerType.Console,
                    CallerRole = "admin",
                    CallerPrivileges = ["*"],
                    FromChatGroupId = GlobalConstants.ConsoleGroup,
                },
            },
            result =>
            {
                if (result.Status == EnumCommandStatus.Deferred)
                {
                    return;
                }

                tcs.TrySetResult(result.StatusMessage ?? string.Empty);
            });
        return tcs.Task;
    }
}
