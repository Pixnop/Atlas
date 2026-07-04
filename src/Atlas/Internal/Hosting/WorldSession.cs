using Atlas.Api;
using Atlas.Internal.Player;
using Atlas.Internal.Scheduling;
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
    private bool _playerJoined;

    /// <summary>Initializes a new instance of the <see cref="WorldSession"/> class.</summary>
    /// <param name="api">The live server API for the running scenario.</param>
    /// <param name="server">The live server instance, needed for the dummy-network test player.</param>
    /// <param name="ticks">The tick source driving <see cref="Ticks"/> and <see cref="Until"/>.</param>
    public WorldSession(ICoreServerAPI api, ServerMain server, TickSource ticks)
    {
        _api = api;
        _server = server;
        _ticks = ticks;
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
    public Entity SpawnEntity(string entityCode, BlockPos pos)
    {
        EntityProperties type = _api.World.GetEntityType(new AssetLocation(entityCode))
            ?? throw new ArgumentException($"Unknown entity code '{entityCode}'", nameof(entityCode));
        Entity entity = _api.ClassRegistry.CreateEntity(type);

        // EntityPos.SetPos(BlockPos) copies X/Y/Z only; it does not read pos.dimension, so the
        // dimension has to be propagated explicitly or the entity always spawns in dimension 0.
        entity.Pos.SetPos(pos);
        entity.Pos.Dimension = pos.dimension;

        _api.World.SpawnEntity(entity);
        return entity;
    }

    /// <inheritdoc/>
    public void ExecuteCommand(string command) => _api.InjectConsole(command);

    /// <inheritdoc/>
    public Task Ticks(int count) => _ticks.WaitTicksAsync(count);

    /// <inheritdoc/>
    public Task Until(Func<bool> predicate, int timeoutTicks = 600) => _ticks.WaitUntilAsync(predicate, timeoutTicks);

    /// <inheritdoc/>
    public async Task<ITestPlayer> JoinPlayer(string name)
    {
        if (_playerJoined)
        {
            throw new AtlasSetupException(
                "JoinPlayer was already called once in this scenario. Atlas only supports one " +
                "test player per world (one dummy-network slot per server); call JoinPlayer once " +
                "and reuse the returned ITestPlayer instead of joining again.");
        }

        _playerJoined = true;
        try
        {
            DummyPlayerConnection connection = DummyClientConnector.Connect(_server, name);

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
            return new TestPlayer(_api, client, _ticks);
        }
        catch
        {
            // Release the claim on any failure: a caller may legitimately want to retry (e.g.
            // after fixing a mod/version mismatch), and a stale _playerJoined = true (or a socket
            // slot DummyClientConnector.Connect claimed but the server later rejected) would make
            // the retry fail with a misleading "already joined" instead of the real cause.
            _playerJoined = false;
            DummyClientConnector.ReleaseSlot(_server);
            throw;
        }
    }

    /// <inheritdoc/>
    public IEntityStats StatsOf(Entity entity) => new EntityStatsView(entity);

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
                    foreach (KeyValuePair<int, ConnectedClient> kvp in _server.Clients)
                    {
                        if (kvp.Value.PlayerName == name)
                        {
                            everSeen = true;
                            if (kvp.Value.Entityplayer != null)
                            {
                                found = kvp.Value;
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
    private AtlasSetupException JoinRejected(string name)
        => new(
            $"Test player '{name}' did not finish joining the world within the tick bound. " +
            "The server rejected the synthetic client join - most likely a game network-version " +
            "drift relative to the Atlas build, or a join-packet change; check the server logs " +
            $"under '{GamePaths.DataPath}'.");
}
