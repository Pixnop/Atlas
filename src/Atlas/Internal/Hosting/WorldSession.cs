using Atlas.Api;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Thin wrapper over the live server API and tick source, exposed to scenarios as
/// <see cref="IWorldSession"/>.</summary>
internal sealed class WorldSession : IWorldSession
{
    private readonly ICoreServerAPI _api;
    private readonly TickSource _ticks;

    /// <summary>Initializes a new instance of the <see cref="WorldSession"/> class.</summary>
    /// <param name="api">The live server API for the running scenario.</param>
    /// <param name="ticks">The tick source driving <see cref="Ticks"/> and <see cref="Until"/>.</param>
    public WorldSession(ICoreServerAPI api, TickSource ticks)
    {
        _api = api;
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
}
