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

/// <summary>EXPERIMENTAL, stage 0 feasibility spike for world snapshot/rollback (see
/// docs/specs/2026-07-06-world-snapshot-rollback.md, option (c)). Snapshots the whole world
/// database through the engine's own open <see cref="GameDatabase"/> after a forced save, and
/// rolls the live world back to that snapshot by recycling every loaded chunk column through the
/// engine's public unload (discard) and load (reload from database) paths.</summary>
/// <remarks>Not a shipping feature: no public API, no fallback wiring, no player support (the
/// spec defers joined test players to stage 2, so a capture with joined players is rejected).
/// Every call runs on the game thread via the host's scheduler. The only engine access beyond
/// the public surface is the boot-validated reflection in <see cref="Create"/> over the two
/// internals the spec names: <c>ServerMain.chunkThread</c> and
/// <c>ChunkServerThread.gameDatabase</c>.</remarks>
internal sealed class WorldRollbackSpike
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

    /// <summary>Initializes a new instance of the <see cref="WorldRollbackSpike"/> class.
    /// Use <see cref="Create"/>; the constructor only stores the already-validated references.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="server">The live embedded server.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <param name="chunkThread">The engine's chunk thread (reflected).</param>
    /// <param name="database">The engine's open game database (reflected).</param>
    private WorldRollbackSpike(
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

    /// <summary>Gets the number of chunk blobs captured by the last <see cref="CaptureAsync"/>.</summary>
    public int SnapshotChunkCount => _chunks?.Count ?? 0;

    /// <summary>Gets the loaded chunk columns recorded by the last <see cref="CaptureAsync"/>.</summary>
    public IReadOnlyList<(int X, int Z)> SnapshotColumns => _columns ?? [];

    /// <summary>Resolves the two engine internals the rollback needs and fails fast with a clear
    /// message if the game version renamed either (the spec's boot-validation requirement).</summary>
    /// <param name="api">The live server API; its <c>World</c> is the embedded <see cref="ServerMain"/>.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <returns>A validated spike instance.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a reflected internal is missing: the
    /// engine layout drifted and rollback cannot work on this game version.</exception>
    public static WorldRollbackSpike Create(ICoreServerAPI api, TickSource ticks)
    {
        var server = (ServerMain)api.World;

        FieldInfo chunkThreadField = typeof(ServerMain).GetField(
            "chunkThread", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AtlasSetupException(
                "World rollback spike: internal field 'ServerMain.chunkThread' not found; the " +
                "engine layout changed on this game version, rollback is not available.");
        var chunkThread = (ChunkServerThread?)chunkThreadField.GetValue(server)
            ?? throw new AtlasSetupException(
                "World rollback spike: 'ServerMain.chunkThread' is null; the server is not fully booted.");

        FieldInfo databaseField = typeof(ChunkServerThread).GetField(
            "gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AtlasSetupException(
                "World rollback spike: internal field 'ChunkServerThread.gameDatabase' not found; " +
                "the engine layout changed on this game version, rollback is not available.");
        var database = (GameDatabase?)databaseField.GetValue(chunkThread)
            ?? throw new AtlasSetupException(
                "World rollback spike: 'ChunkServerThread.gameDatabase' is null; the savegame is not open.");

        return new WorldRollbackSpike(api, server, ticks, chunkThread, database);
    }

    /// <summary>Snapshots the world: forces one full save, waits for its off-thread half to
    /// settle, then reads every blob back through the open database.</summary>
    /// <returns>A task that completes when the snapshot is in memory.</returns>
    /// <remarks>Also quiets the two background database writers for the host's lifetime, per the
    /// spec's concurrency notes: autosave via the public <see cref="MagicNum.ServerAutoSave"/>
    /// knob, and the background chunk unloader (which persists dirty chunks it evicts, unlike the
    /// discard path rollback uses) via the engine's own /chunk unload toggle.</remarks>
    public async Task CaptureAsync()
    {
        if (_server.Clients.Count > 0)
        {
            throw new AtlasSetupException(
                "World rollback spike: capture with joined test players is out of scope for " +
                "stage 0 (the spec defers players to stage 2).");
        }

        // Quiet the background writers: no timed autosave, no dirty-persisting background unloads.
        MagicNum.ServerAutoSave = 0;
        string unloadMessage = await ExecuteConsoleAsync("/chunk unload false").ConfigureAwait(true);
        if (!unloadMessage.Contains("off", StringComparison.Ordinal))
        {
            throw new AtlasSetupException(
                $"World rollback spike: could not pause background chunk unloading: '{unloadMessage}'.");
        }

        // Force one save so the database is a complete image of the live world, and wait for the
        // off-thread half (dirty chunks, map chunks, savegame blob) to be written out.
        await WaitForSaveIdleAsync("before the forced save").ConfigureAwait(true);
        string saveMessage = await ExecuteConsoleAsync("/autosavenow").ConfigureAwait(true);
        if (!saveMessage.Contains("Autosave completed", StringComparison.Ordinal))
        {
            throw new AtlasSetupException(
                $"World rollback spike: the engine skipped the forced save: '{saveMessage}'.");
        }

        await WaitForSaveIdleAsync("after the forced save").ConfigureAwait(true);

        // Read the complete database into memory through the already-open connection.
        _chunks = CloneTable(_database.GetAllChunks());
        _chunkIndices = IndexSet(_chunks);
        _mapChunks = CloneTable(_database.GetAllMapChunks());
        _mapChunkIndices = IndexSet(_mapChunks);
        _mapRegions = CloneTable(_database.GetAllMapRegions());
        _mapRegionIndices = IndexSet(_mapRegions);
        _saveGame = _database.GetSaveGame();
        _columns = LoadedColumns();
    }

    /// <summary>Rolls the live world back to the last snapshot: unloads every loaded chunk column
    /// through the discarding public path, restores the database blobs and in-memory globals, then
    /// reloads the snapshot's column set and pumps until fully loaded.</summary>
    /// <returns>A task that completes when every snapshot column is fully loaded again.</returns>
    public async Task RollbackAsync()
    {
        if (_chunks == null || _chunkIndices == null || _mapChunks == null || _mapChunkIndices == null
            || _mapRegions == null || _mapRegionIndices == null || _saveGame == null || _columns == null)
        {
            throw new InvalidOperationException("CaptureAsync must complete before RollbackAsync.");
        }

        await WaitForSaveIdleAsync("before rollback").ConfigureAwait(true);

        // 1. Discard all live world state: the public UnloadChunkColumn path despawns entities,
        //    unloads block entities, fires the mod unload events, and never persists anything.
        foreach ((int x, int z) in LoadedColumns())
        {
            _api.WorldManager.UnloadChunkColumn(x, z);
        }

        if (_server.LoadedChunkIndices.Length != 0)
        {
            throw new AtlasSetupException(
                $"World rollback spike: {_server.LoadedChunkIndices.Length} chunks still loaded " +
                "after unloading every column; unload path did not behave as the spec assumes.");
        }

        // 2. Restore the database: delete rows the polluted world added, write the snapshot back.
        DeleteExtraRows();
        _database.SetChunks(_chunks);
        _database.SetMapChunks(_mapChunks);
        _database.SetMapRegions(_mapRegions);
        _database.StoreSaveGame(_saveGame);

        // 3. Restore in-memory globals on the live SaveGame instance and roll the clock back.
        RestoreGlobals();

        // 4. Reload the snapshot's columns from the restored database and pump until done.
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
                $"World rollback spike: save machinery still busy {stage} " +
                $"(readyToAutoSave={_server.readyToAutoSave}, runOffThreadSaveNow={_chunkThread.runOffThreadSaveNow}).",
                ex);
        }
    }

    /// <summary>Reads the currently loaded chunk columns from the public loaded-chunk index list.</summary>
    /// <returns>The distinct loaded column coordinates, dimension 0 only.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a loaded chunk lives in a mini-dimension:
    /// out of scope for stage 0 (the spec defers dimensions to stage 3).</exception>
    private List<(int X, int Z)> LoadedColumns()
    {
        var columns = new HashSet<(int X, int Z)>();
        foreach (long index in _server.LoadedChunkIndices)
        {
            ChunkPos pos = _server.WorldMap.ChunkPosFromChunkIndex3D(index);
            if (pos.Dimension != 0)
            {
                throw new AtlasSetupException(
                    $"World rollback spike: loaded chunk in dimension {pos.Dimension}; " +
                    "mini-dimensions are out of scope for stage 0 (spec stage 3).");
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
