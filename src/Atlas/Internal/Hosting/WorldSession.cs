using Atlas.Api;
using Atlas.Internal.Player;
using Atlas.Internal.Scheduling;
using Atlas.Internal.Staging;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Thin wrapper over the live server API and tick source, exposed to scenarios as
/// <see cref="IWorldSession"/>.</summary>
internal sealed class WorldSession : IWorldSession
{
    private readonly ICoreServerAPI _api;
    private readonly ServerMain _server;
    private readonly TickSource _ticks;
    private readonly HashSet<string> _joinedNames;
    private readonly string _modBaseDir;

    /// <summary>Initializes a new instance of the <see cref="WorldSession"/> class.</summary>
    /// <param name="api">The live server API for the running scenario.</param>
    /// <param name="server">The live server instance, needed for the dummy-network test player.</param>
    /// <param name="ticks">The tick source driving <see cref="Ticks"/> and <see cref="Until"/>.</param>
    /// <param name="joinedNames">The host-owned registry of already-joined test player names.
    /// Host-owned because joined players stay connected for the host's lifetime, while a
    /// <see cref="WorldSession"/> only lives for one scenario: the duplicate-name guard in
    /// <see cref="JoinPlayer"/> has to see names joined by earlier scenarios on the same host.</param>
    /// <param name="modBaseDir">Base directory for resolving relative schematic paths in
    /// <see cref="PlaceSchematic(string, BlockPos)"/>, the same one the host resolves relative
    /// mod and fixture paths against.</param>
    public WorldSession(
        ICoreServerAPI api,
        ServerMain server,
        TickSource ticks,
        HashSet<string> joinedNames,
        string modBaseDir)
    {
        _api = api;
        _server = server;
        _ticks = ticks;
        _joinedNames = joinedNames;
        _modBaseDir = modBaseDir;
    }

    /// <inheritdoc/>
    public ICoreServerAPI Api => _api;

    /// <inheritdoc/>
    public IGameCalendar Calendar => _api.World.Calendar;

    /// <inheritdoc/>
    public BlockPos Spawn
    {
        get
        {
            BlockPos p = _api.World.DefaultSpawnPosition.AsBlockPos;
            return new BlockPos(p.X, _api.World.BlockAccessor.GetTerrainMapheightAt(p), p.Z, p.dimension);
        }
    }

    /// <inheritdoc/>
    public Block BlockAt(BlockPos pos) => _api.World.BlockAccessor.GetBlock(pos);

    /// <inheritdoc/>
    public T? BlockEntityAt<T>(BlockPos pos)
        where T : BlockEntity
        => _api.World.BlockAccessor.GetBlockEntity(pos) as T;

    /// <inheritdoc/>
    public IReadOnlyList<Entity> EntitiesIn(Cuboidi area) => EntitiesIn(new WorldArea(area, Dimension: 0));

    /// <inheritdoc/>
    public IReadOnlyList<Entity> EntitiesIn(WorldArea area)
    {
        (BlockPos start, BlockPos end) = WorldArea.Corners(area);
        return _api.World.GetEntitiesInsideCuboid(start, end);
    }

    /// <inheritdoc/>
    public void SetBlock(string blockCode, BlockPos pos)
    {
        Block block = _api.World.GetBlock(new AssetLocation(blockCode))
            ?? throw new ArgumentException($"Unknown block code '{blockCode}'", nameof(blockCode));
        _api.World.BlockAccessor.SetBlock(block.BlockId, pos);
    }

    /// <inheritdoc/>
    public int PlaceSchematic(string path, BlockPos origin)
        => PlaceSchematicCore(path, origin, mode: null);

    /// <inheritdoc/>
    public int PlaceSchematic(string path, BlockPos origin, EnumReplaceMode mode)
        => PlaceSchematicCore(path, origin, mode);

    /// <inheritdoc/>
    public Entity SpawnEntity(string entityCode, BlockPos pos)
    {
        EntityProperties type = _api.World.GetEntityType(new AssetLocation(entityCode))
            ?? throw new ArgumentException($"Unknown entity code '{entityCode}'", nameof(entityCode));
        Entity entity = _api.ClassRegistry.CreateEntity(type);

        // EntityPos.SetPos(BlockPos) copies X/Y/Z only; it does not read pos.dimension, so the
        // dimension has to be propagated explicitly or the entity always spawns in dimension 0.
        // Written through ServerPos, not Pos: pre-1.22 engines keep them as two separate
        // instances and SpawnEntity's chunk registration reads ServerPos, so a Pos-only write
        // would land the entity in the chunk at the origin; on 1.22 both names are one instance.
        // Pos is then mirrored so the client-side copy starts real on pre-1.22 too. (SidedPos is
        // unusable here: it dereferences entity.World, which is unset until SpawnEntity.)
        // CS0618: 1.22 marks ServerPos obsolete as an alias of Pos; it exists on every
        // supported version and IS the pre-1.22 compatibility surface.
#pragma warning disable CS0618
        entity.ServerPos.SetPos(pos);
        entity.ServerPos.Dimension = pos.dimension;
        entity.Pos.SetFrom(entity.ServerPos);
#pragma warning restore CS0618

        _api.World.SpawnEntity(entity);
        return entity;
    }

    /// <inheritdoc/>
    public Task<CommandResult> ExecuteCommand(string command)
    {
        if (string.IsNullOrEmpty(command) || command[0] != '/')
        {
            throw new ArgumentException(
                $"Command '{command}' must start with a slash: the engine's command dispatch " +
                "strips the first character unconditionally, so a slashless command would be " +
                "silently misparsed.",
                nameof(command));
        }

        var tcs = new TaskCompletionSource<CommandResult>();
        _api.ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs
            {
                // The exact caller the engine builds for its own server-console commands.
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
                // A command whose argument parsing goes async reports Deferred first and calls
                // back again with the real outcome once the handler has run; only that final
                // result is the command's outcome.
                if (result.Status == EnumCommandStatus.Deferred)
                {
                    return;
                }

                bool ok = result.Status == EnumCommandStatus.Success;
                string message = result.StatusMessage == null
                    ? string.Empty
                    : Lang.Get(result.StatusMessage, result.MessageParams ?? []);

                // Some engine failures (e.g. an unknown command) carry only an error code and no
                // message; synthesize one so a scenario's Assert.True(result.Ok, result.Message)
                // still names the failure instead of printing an empty string.
                if (!ok && message.Length == 0)
                {
                    message = $"Command '{command}' failed with status '{result.Status}'" +
                        (string.IsNullOrEmpty(result.ErrorCode) ? "." : $" and error code '{result.ErrorCode}'.");
                }

                tcs.TrySetResult(new CommandResult(ok, message, result));
            });
        return tcs.Task;
    }

    /// <inheritdoc/>
    public Task Ticks(int count) => _ticks.WaitTicksAsync(count);

    /// <inheritdoc/>
    public Task Until(Func<bool> predicate, int timeoutTicks = 600) => _ticks.WaitUntilAsync(predicate, timeoutTicks);

    /// <inheritdoc/>
    public async Task<ITestPlayer> JoinPlayer(string name)
    {
        if (!_joinedNames.Add(name))
        {
            throw new AtlasSetupException(
                $"A test player named '{name}' already joined this class's world (the class " +
                "host is shared by every scenario in the class, so an earlier scenario's player " +
                "is still connected). Reuse the ITestPlayer it got back (share it via a field), " +
                "join under a different name, or isolate the scenario with " +
                "[AtlasScenario(FreshWorld = true)]. Rejected up front because the server would " +
                "treat the duplicate as the same account reconnecting and kick the first player " +
                "mid-scenario.");
        }

        DummyPlayerConnection? claimed = null;
        try
        {
            DummyPlayerConnection connection = DummyClientConnector.Connect(_server, name);
            claimed = connection;

            ConnectedClient client = await WaitForJoin(name).ConfigureAwait(true);
            DummyClientConnector.RegisterUdpEndpoint(connection, client.Id);

            // FinalizePlayerIdentification schedules a background check ~500ms later (off the
            // game thread, via the engine's own thread pool) that warns and sends a
            // fallback-to-TCP packet if the client never sent real UDP traffic - which a dummy
            // player never does. Marking ServerDidReceiveUdp up front skips that branch
            // entirely: the background task still runs, but takes the "client did send UDP"
            // no-op path instead of touching the connection, so there is nothing left to race
            // against a short-lived scenario's shutdown.
            client.ServerDidReceiveUdp = true;

            // A mod-under-test may kick this player, and kicks issued off the game thread abort
            // the engine's own teardown halfway (leaving a zombie client with a still-ticking
            // entity - see KickedPlayerCleanup). Arm the game-thread completion BEFORE sending
            // RequestJoin: PlayerJoin fires inside the engine's request-join handling, so a mod
            // kicking from its PlayerJoin handler (or a background continuation of it) can fire
            // PlayerDisconnect at any point after that packet is processed, and a
            // PlayerDisconnect that fires before the cleanup's subscription exists is
            // unobservable (arming after the inventory wait lost exactly that race on slow CI
            // runners). The cleanup also frees the joined-name claim so the scenario can rejoin
            // under the same name after a kick.
            KickedPlayerCleanup.Arm(_api, _server, client, connection, () => _joinedNames.Remove(name));

            // Packet 11 (RequestJoin) wires up the player's InventoryManager (HandleRequestJoin
            // calls into every registered server system's OnPlayerJoin); it must be sent after
            // the entity has spawned, since HandleRequestJoin reads ConnectedClient.Entityplayer
            // immediately.
            DummyClientConnector.RequestJoin(connection);
            await _ticks.WaitUntilAsync(() => client.Player.InventoryManager.Inventories.Count > 0, timeoutTicks: 100).ConfigureAwait(true);

            // NOTE: the server pushes gameplay state (chunk data, entity updates) to the joined
            // client over the same dummy UDP/TCP endpoints for as long as the scenario runs.
            // Nothing ever reads that outbound traffic back off the dummy buffers (the test
            // player has no real socket draining it), so it simply accumulates there; bounded by
            // the scenario's own lifetime, so this is not an unbounded leak in practice.
            return new TestPlayer(_api, _server, client, _ticks);
        }
        catch
        {
            // Release the claim on any failure: a caller may legitimately want to retry (e.g.
            // after fixing a mod/version mismatch), and a stale joined-name entry (or a socket
            // slot DummyClientConnector.Connect claimed but the server later rejected) would make
            // the retry fail with a misleading "already joined" instead of the real cause. Only
            // a claim that Connect actually returned is released - releasing anything else could
            // detach a slot belonging to an earlier, still-connected player (Connect self-cleans
            // when it throws past its own claim).
            _joinedNames.Remove(name);
            if (claimed is { } connection)
            {
                DummyClientConnector.ReleaseSlot(_server, connection);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public IEntityStats StatsOf(Entity entity) => new EntityStatsView(entity);

    /// <summary>Loads and places a schematic, mirroring the engine's worldedit import sequence:
    /// <c>LoadFromFile</c>, <c>Init</c>, <c>Place</c> (which also places block entities and
    /// stored entities for a plain, non-revertable block accessor), then <c>PlaceDecors</c>.</summary>
    /// <param name="path">Relative or absolute path to the schematic file.</param>
    /// <param name="origin">Where the schematic's minimum X/Y/Z corner is placed.</param>
    /// <param name="mode">The replace mode to place with, or <see langword="null"/> for the one
    /// stored in the schematic itself (the engine's own default when no mode is given).</param>
    /// <returns>The number of blocks placed.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the engine cannot load the file.</exception>
    private int PlaceSchematicCore(string path, BlockPos origin, EnumReplaceMode? mode)
    {
        string resolved = SchematicFiles.Resolve(path, _modBaseDir);
        string error = string.Empty;
        BlockSchematic? schematic = BlockSchematic.LoadFromFile(resolved, ref error);
        if (schematic == null)
        {
            throw new AtlasSetupException(SchematicFiles.LoadFailureMessage(path, resolved, error));
        }

        IBlockAccessor accessor = _api.World.BlockAccessor;
        schematic.Init(accessor);
        int placed = schematic.Place(
            accessor, _api.World, origin, mode ?? schematic.ReplaceMode, replaceMetaBlocks: true);
        schematic.PlaceDecors(accessor, origin);
        return placed;
    }

    /// <summary>Waits until the server has registered a client under <paramref name="name"/> with
    /// a spawned entity.</summary>
    /// <param name="name">The player name to look for.</param>
    /// <returns>The matching <see cref="ConnectedClient"/>.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the client is observed once and then
    /// disappears from <see cref="ServerMain.Clients"/> (the server accepted, then dropped, the
    /// connection - e.g. <c>DisconnectPlayer</c> ran), or when the tick bound expires without the
    /// client ever appearing with a spawned entity. Both symptoms are indistinguishable from a
    /// generic timeout to a caller, but share the same most-likely root cause: the embedded
    /// server rejected the synthetic client join.</exception>
    private async Task<ConnectedClient> WaitForJoin(string name)
    {
        ConnectedClient? found = null;
        bool everSeen = false;
        try
        {
            await _ticks.WaitUntilAsync(
                () =>
                {
                    foreach (ConnectedClient client in _server.Clients.Select(kvp => kvp.Value))
                    {
                        if (client.PlayerName == name)
                        {
                            everSeen = true;
                            if (client.Entityplayer != null)
                            {
                                found = client;
                                return true;
                            }

                            return false;
                        }
                    }

                    // The client was seen at least once but is no longer in server.Clients at
                    // all: the server disconnected it (DisconnectPlayer removes the entry
                    // outright) before its entity ever spawned.
                    if (everSeen)
                    {
                        throw JoinRejected(name);
                    }

                    return false;
                },
                timeoutTicks: 100).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException)
        {
            throw JoinRejected(name);
        }

        return found!;
    }

    /// <summary>Builds the actionable diagnosis for a rejected or never-observed synthetic join.</summary>
    /// <param name="name">The player name that failed to join.</param>
    /// <returns>The exception to throw in place of a bare timeout.</returns>
    private static AtlasSetupException JoinRejected(string name)
        => new(
            $"Test player '{name}' did not finish joining the world within the tick bound. " +
            "The server rejected the synthetic client join - most likely an invalid player name " +
            "(the engine only accepts letters, digits, underscores and dashes, 16 characters at " +
            "most), otherwise a game network-version drift relative to the Atlas build, or a " +
            $"join-packet change; check the server logs under '{GamePaths.DataPath}'.");
}
