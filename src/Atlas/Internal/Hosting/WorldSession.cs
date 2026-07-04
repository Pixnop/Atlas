using Atlas.Api;
using Atlas.Internal.Player;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
                "A second headless test player was requested, but Atlas only supports one test " +
                "player per world right now. The dummy-network mechanism claims a single, " +
                "fixed-size socket slot on the embedded server (the same slot a real singleplayer " +
                "client would use), and that slot is single-occupancy; concurrent multiple test " +
                "players needs its own spike to multiplex several dummy connections into that " +
                "slot, and is tracked as follow-up work (see issue #4), not supported yet.");
        }

        _playerJoined = true;
        DummyPlayerConnection connection = DummyClientConnector.Connect(_server, name);

        ConnectedClient client = await WaitForJoin(name).ConfigureAwait(true);
        DummyClientConnector.RegisterUdpEndpoint(connection, client.Id);

        // FinalizePlayerIdentification schedules a background check ~500ms later (off the game
        // thread, via the engine's own thread pool) that warns and sends a fallback-to-TCP packet
        // if the client never sent real UDP traffic - which a dummy player never does. Marking
        // ServerDidReceiveUdp up front skips that branch entirely: the background task still
        // runs, but takes the "client did send UDP" no-op path instead of touching the
        // connection, so there is nothing left to race against a short-lived scenario's shutdown.
        client.ServerDidReceiveUdp = true;

        // Packet 11 (RequestJoin) wires up the player's InventoryManager (HandleRequestJoin calls
        // into every registered server system's OnPlayerJoin); it must be sent after the entity
        // has spawned, since HandleRequestJoin reads ConnectedClient.Entityplayer immediately.
        DummyClientConnector.RequestJoin(connection);
        await _ticks.WaitUntilAsync(() => client.Player.InventoryManager.Inventories.Count > 0, timeoutTicks: 100).ConfigureAwait(true);

        return new TestPlayer(_api, client);
    }

    /// <inheritdoc/>
    public IEntityStats StatsOf(Entity entity) => new EntityStatsView(entity);

    /// <summary>Waits until the server has registered a client under <paramref name="name"/> with
    /// a spawned entity.</summary>
    /// <param name="name">The player name to look for.</param>
    /// <returns>The matching <see cref="ConnectedClient"/>.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when the client never appears with a
    /// spawned entity within the timeout.</exception>
    private async Task<ConnectedClient> WaitForJoin(string name)
    {
        ConnectedClient? found = null;
        await _ticks.WaitUntilAsync(
            () =>
            {
                foreach (KeyValuePair<int, ConnectedClient> kvp in _server.Clients)
                {
                    if (kvp.Value.PlayerName == name && kvp.Value.Entityplayer != null)
                    {
                        found = kvp.Value;
                        return true;
                    }
                }

                return false;
            },
            timeoutTicks: 100).ConfigureAwait(true);

        return found!;
    }
}
