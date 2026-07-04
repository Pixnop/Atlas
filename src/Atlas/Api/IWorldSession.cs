using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Api;

/// <summary>Author-facing world surface. Every member runs on the game thread.</summary>
public interface IWorldSession
{
    /// <summary>Gets the live server API. Escape hatch for anything not covered by this surface.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    ICoreServerAPI Api { get; }

    /// <summary>Gets the default spawn position, resolved to terrain height.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    BlockPos Spawn { get; }

    /// <summary>Gets the world's game calendar.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    IGameCalendar Calendar { get; }

    /// <summary>Gets the block at the given position.</summary>
    /// <param name="pos">The position to query.</param>
    /// <returns>The block at <paramref name="pos"/>.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    Block BlockAt(BlockPos pos);

    /// <summary>Gets the block entity of type <typeparamref name="T"/> at the given position, if any.</summary>
    /// <typeparam name="T">The expected block entity type.</typeparam>
    /// <param name="pos">The position to query.</param>
    /// <returns>The block entity at <paramref name="pos"/> cast to <typeparamref name="T"/>, or
    /// <see langword="null"/> if there is none or it does not match.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    T? BlockEntityAt<T>(BlockPos pos)
        where T : BlockEntity;

    /// <summary>Gets every entity inside the given area, in dimension 0.</summary>
    /// <param name="area">The cuboid area to query, in dimension 0.</param>
    /// <returns>The entities found inside <paramref name="area"/>.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    IReadOnlyList<Entity> EntitiesIn(Cuboidi area);

    /// <summary>Gets every entity inside the given area, in its own dimension.</summary>
    /// <param name="area">The cuboid area and dimension to query.</param>
    /// <returns>The entities found inside <paramref name="area"/>.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    IReadOnlyList<Entity> EntitiesIn(WorldArea area);

    /// <summary>Sets the block at the given position.</summary>
    /// <param name="blockCode">The block's asset location code, e.g. <c>"game:soil-medium-normal"</c>.</param>
    /// <param name="pos">The position to set.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blockCode"/> does not resolve
    /// to a known block.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    void SetBlock(string blockCode, BlockPos pos);

    /// <summary>Spawns an entity of the given type at the given position.</summary>
    /// <param name="entityCode">The entity's asset location code.</param>
    /// <param name="pos">The position to spawn at.</param>
    /// <returns>The spawned entity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityCode"/> does not resolve
    /// to a known entity type.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Entity SpawnEntity(string entityCode, BlockPos pos);

    /// <summary>Runs a server console command, e.g. <c>"/time set day"</c>.</summary>
    /// <param name="command">The command text, including the leading slash.</param>
    /// <remarks>Runs on the game thread.</remarks>
    void ExecuteCommand(string command);

    /// <summary>Waits for a number of ticks to elapse.</summary>
    /// <param name="count">The number of ticks to wait.</param>
    /// <returns>A task that completes once <paramref name="count"/> ticks have elapsed.</returns>
    Task Ticks(int count);

    /// <summary>Waits until a predicate becomes true, polled once per tick, or a timeout elapses.</summary>
    /// <param name="predicate">The condition to poll.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait before giving up.</param>
    /// <returns>A task that completes when <paramref name="predicate"/> is true.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when <paramref name="timeoutTicks"/> elapses
    /// without <paramref name="predicate"/> becoming true.</exception>
    Task Until(Func<bool> predicate, int timeoutTicks = 600);

    /// <summary>Joins a headless test player into the world.</summary>
    /// <param name="name">The player name to join as.</param>
    /// <returns>The joined player, once its entity has spawned in the world.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a test player is already joined: Atlas
    /// joins headless players over the same in-memory dummy-network mechanism the game's own
    /// singleplayer client uses to talk to its local server, which claims a single, fixed-size
    /// socket slot on the embedded server. That slot is single-occupancy, so only one headless
    /// test player is supported per world; concurrent multiple test players would need a
    /// multiplexing shim over that slot (tracked as follow-up work, see issue #4), not supported
    /// yet.</exception>
    /// <remarks>Runs on the game thread. Backed by the same dummy-network mechanism the game's
    /// own singleplayer client uses, bypassing auth entirely (recognized as a local connection,
    /// same as real singleplayer) - see <c>ITestPlayer</c> remarks for what that does and does
    /// not cover.</remarks>
    Task<ITestPlayer> JoinPlayer(string name);

    /// <summary>Gets a read-only stats view over any entity, for assertions.</summary>
    /// <param name="entity">The entity to read stats from.</param>
    /// <returns>The stats view.</returns>
    /// <remarks>Runs on the game thread. Works for any entity, not just players - e.g. a
    /// creature spawned via <see cref="SpawnEntity"/>.</remarks>
    IEntityStats StatsOf(Entity entity);
}
