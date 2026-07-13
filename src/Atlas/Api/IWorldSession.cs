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

    /// <summary>Loads a block schematic (<c>.json</c>, e.g. a worldedit export) and places it
    /// with its minimum X/Y/Z corner at <paramref name="origin"/>, using the replace mode stored
    /// in the schematic itself (<see cref="EnumReplaceMode.ReplaceAllNoAir"/> unless the
    /// exporting tool chose otherwise).</summary>
    /// <param name="path">Path to the schematic file, with or without the <c>.json</c>
    /// extension. Absolute, or relative to the same base directory as mod paths and
    /// <see cref="WorldOptions.SaveFile"/> (for scenario classes, the test assembly's
    /// directory).</param>
    /// <param name="origin">Where the schematic's minimum X/Y/Z corner is placed; blocks extend
    /// toward positive X, Y and Z from here, in this position's dimension.</param>
    /// <returns>The number of blocks placed.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the file does not exist or does not
    /// parse as a schematic; the message carries the resolved path and the engine's error.</exception>
    /// <remarks>Runs on the game thread. Mirrors the engine's worldedit import: places the
    /// schematic's blocks, decors, block entities (with their saved data) and stored entities.
    /// Complements <c>[AtlasWorld(SaveFile = ...)]</c>, which loads a whole prebuilt world;
    /// this places a single prebuilt structure into the running world.</remarks>
    int PlaceSchematic(string path, BlockPos origin);

    /// <summary>Loads a block schematic (<c>.json</c>, e.g. a worldedit export) and places it
    /// with its minimum X/Y/Z corner at <paramref name="origin"/>, using
    /// <paramref name="mode"/> instead of the replace mode stored in the schematic. E.g.
    /// <see cref="EnumReplaceMode.ReplaceAll"/> stamps the schematic's full cuboid, clearing
    /// existing blocks where the schematic has air.</summary>
    /// <param name="path">Path to the schematic file, with or without the <c>.json</c>
    /// extension. Absolute, or relative to the same base directory as mod paths and
    /// <see cref="WorldOptions.SaveFile"/> (for scenario classes, the test assembly's
    /// directory).</param>
    /// <param name="origin">Where the schematic's minimum X/Y/Z corner is placed; blocks extend
    /// toward positive X, Y and Z from here, in this position's dimension.</param>
    /// <param name="mode">The replace mode to place with, overriding the schematic's own.</param>
    /// <returns>The number of blocks placed.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the file does not exist or does not
    /// parse as a schematic; the message carries the resolved path and the engine's error.</exception>
    /// <remarks>Runs on the game thread. Mirrors the engine's worldedit import: places the
    /// schematic's blocks, decors, block entities (with their saved data) and stored entities.</remarks>
    int PlaceSchematic(string path, BlockPos origin, EnumReplaceMode mode);

    /// <summary>Spawns an entity of the given type at the given position.</summary>
    /// <param name="entityCode">The entity's asset location code.</param>
    /// <param name="pos">The position to spawn at.</param>
    /// <returns>The spawned entity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityCode"/> does not resolve
    /// to a known entity type.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Entity SpawnEntity(string entityCode, BlockPos pos);

    /// <summary>Runs a server command as the console (admin role, every privilege), e.g.
    /// <c>"/time set day"</c>, and returns its outcome.</summary>
    /// <param name="command">The command text, including the leading slash.</param>
    /// <returns>The command's outcome: success flag, resolved status message, and the engine's
    /// raw <c>TextCommandResult</c> as an escape hatch.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> does not start
    /// with a slash: the engine's command dispatch strips the first character unconditionally, so
    /// a slashless command would be silently misparsed instead of failing loudly.</exception>
    /// <remarks>Runs on the game thread. Commands whose argument parsing goes async (e.g. player
    /// lookups) complete on a later tick; the returned task follows them to their final result.
    /// An unknown command completes with <c>Ok = false</c> rather than throwing, so scenarios can
    /// assert on intentional failures.</remarks>
    Task<CommandResult> ExecuteCommand(string command);

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

    /// <summary>Joins a headless test player into the world. Multiple players can be joined into
    /// the same world, each under its own name.</summary>
    /// <param name="name">The player name to join as. Must be unique within the world: the
    /// server identifies accounts by a name-derived UID, so a duplicate would be treated as the
    /// same account reconnecting and kick the first player.</param>
    /// <returns>The joined player, once its entity has spawned in the world.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a test player with the same name is
    /// already joined in this world - including by an earlier scenario in the same class, since
    /// the class host (and its world) is shared by every scenario in the class.</exception>
    /// <remarks>Runs on the game thread. Backed by the same dummy-network mechanism the game's
    /// own singleplayer client uses, bypassing auth entirely (recognized as a local connection,
    /// same as real singleplayer) - see <c>ITestPlayer</c> remarks for what that does and does
    /// not cover. Each player rides its own dummy socket on the embedded server, so joined
    /// players coexist and act independently in the same world. The join runs the engine's full
    /// sequence, so the returned player has reached the <c>Playing</c> client state: server code
    /// filtering on <c>ConnectedClient.IsPlayingClient</c> (distance-based throttling, playing
    /// counts, <c>GetPlayersAround</c>/<c>NearestPlayer</c>) sees it, the engine's
    /// <c>PlayerNowPlaying</c>/<c>PlayerReady</c> events fire, the join is announced in chat,
    /// and the server streams world updates to the player (into inert dummy buffers). One
    /// exception keeps kick testing possible: a mod kicking the player DURING the join (e.g.
    /// from its PlayerJoin handler) is tolerated - JoinPlayer still returns, the player never
    /// reaches <c>Playing</c>, and the kick is observed via <c>ITestPlayer.IsConnected</c>.</remarks>
    Task<ITestPlayer> JoinPlayer(string name);

    /// <summary>Gets a read-only stats view over any entity, for assertions.</summary>
    /// <param name="entity">The entity to read stats from.</param>
    /// <returns>The stats view.</returns>
    /// <remarks>Runs on the game thread. Works for any entity, not just players - e.g. a
    /// creature spawned via <see cref="SpawnEntity"/>.</remarks>
    IEntityStats StatsOf(Entity entity);
}
