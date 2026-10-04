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

    /// <summary>Gets the number of entity-simulation ticks the embedded server has run since it
    /// booted: the real simulation progress that drives <c>Entity.OnGameTick</c>, read from the
    /// engine's own tick record. Monotonic, so an entity-tick-frequency probe can assert exact
    /// counts against a delta of this property, where a delta of <see cref="Ticks"/> only
    /// approximates it (harness ticks and entity simulation coincide 1:1 under the engine's
    /// default pacing, but the engine does not guarantee the ratio; see
    /// https://github.com/Pixnop/Atlas/wiki/Writing-Scenarios#the-tick-contract).</summary>
    /// <exception cref="AtlasSetupException">Thrown when the loaded engine's tick machinery
    /// drifted and the counter could not attach at boot; the message names the drifted
    /// symbols.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    // The measured contract behind that wiki page: docs/specs/2026-07-14-tick-contract.md.
    long EntitySimulationTicks { get; }

    /// <summary>Gets the number of harness ticks counted so far on this world's host: the unit
    /// of <see cref="Ticks"/> and <see cref="Until"/>, and of the <c>Tick</c> that the entity,
    /// player-data and group records of <see cref="ITestPlayer.Client"/> carry.</summary>
    /// <remarks>Runs on the game thread. One tick is one fire of the engine's game-tick listener,
    /// at most one per <c>ServerMain.Process()</c> pass (see <see cref="Ticks"/>). The count
    /// belongs to the host, not to the class: it restarts at 0 when a new host boots, which is
    /// what <c>[AtlasScenario(RestartWorld = true)]</c> and <c>[AtlasScenario(FreshWorld = true)]</c>
    /// do, so a value read before one is not comparable with a value read after it. A
    /// <c>RollbackWorld</c> restore keeps the host and the count, unless it degrades to a full
    /// recycle, which boots a new host.</remarks>
    int CurrentTick { get; }

    /// <summary>Gets every engine log entry at <see cref="EnumLogType.Warning"/> level or above,
    /// oldest first, recorded since the start of the boot: a malformed JSON asset, an
    /// unresolved recipe ingredient, a mod's own startup warning, anything the engine or a
    /// loaded mod logged through <c>ILogger</c>. The engine's own <c>"Server overloaded. A tick
    /// took Nms to complete."</c> warning is never recorded: it reports machine load, not a
    /// problem with any mod. Keeps growing for as long as the class host is
    /// alive, scenario time included, so a scenario asserting on this sees its own warnings too,
    /// not only the boot's.</summary>
    /// <remarks>Runs on the game thread. Read-only: nothing clears it, and nothing needs to,
    /// since each scenario class gets its own host and its own list. To keep only the entries the
    /// boot itself logged, filter on <see cref="BootDiagnosticEntry.Tick"/>, which is
    /// <see langword="null"/> for an entry logged before the world was ready and the
    /// <see cref="CurrentTick"/> it was logged at otherwise
    /// (<c>World.BootDiagnostics.Where(e =&gt; e.Tick is null)</c>). See
    /// <c>[AtlasWorld(StrictBootDiagnostics = true)]</c> for failing the boot outright on a
    /// non-empty list instead of reading it here.</remarks>
    IReadOnlyList<BootDiagnosticEntry> BootDiagnostics { get; }

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

    /// <summary>Spawns an entity of the given type at the given position, in that position's
    /// dimension.</summary>
    /// <param name="entityCode">The entity's asset location code.</param>
    /// <param name="pos">The position to spawn at, including dimension.</param>
    /// <returns>The spawned entity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="entityCode"/> does not resolve
    /// to a known entity type.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Entity SpawnEntity(string entityCode, BlockPos pos);

    /// <summary>Reads where an entity is: a copy of its server-side position, dimension
    /// included.</summary>
    /// <param name="entity">A spawned entity, such as one returned by <see cref="SpawnEntity"/>
    /// or an <see cref="ITestPlayer.Entity"/>.</param>
    /// <returns>A copy of the entity's position. Writing to it moves nothing, and the entity
    /// moving later does not change it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity"/> is
    /// <see langword="null"/>.</exception>
    /// <remarks>Runs on the game thread. Reads the server-authoritative position the same way on
    /// every supported game version: 1.22 turned <c>Entity.Pos</c> and <c>Entity.ServerPos</c>
    /// from fields into properties that share one instance, while before that they were two
    /// separate instances with <c>ServerPos</c> the server-authoritative one, so a test that reads
    /// <c>entity.Pos</c> directly gets a stale position on one line and a binary that does not
    /// load on the other. Unlike <see cref="EntitiesIn(WorldArea)"/>, which only lists an entity
    /// in its new chunk once the engine's once-a-second pass has re-indexed it, this sees a move
    /// the pass it happens in.</remarks>
    EntityPos PositionOf(Entity entity);

    /// <summary>Waits until an entity's position satisfies a predicate, polled once per tick, and
    /// returns the position that did. Meant for an entity that something else moves: the
    /// engine's own teleport of a creature, or a mod re-homing it, possibly into another
    /// dimension.</summary>
    /// <param name="entity">A spawned entity. The wait holds this reference: it follows the
    /// entity's position, so it does not see a mod that replaces the entity with a new instance
    /// (that entity has to be found again, for example through <see cref="EntitiesIn(WorldArea)"/>).</param>
    /// <param name="arrived">The condition on the entity's position, for example
    /// <c>p =&gt; p.Dimension == 1</c> or <c>p =&gt; p.XYZ.DistanceTo(target) &lt; 1</c>. First
    /// evaluated on the tick after the call, never before this method returns, so a position that
    /// already satisfies it still costs one tick.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait before giving up. Must be
    /// at least 1.</param>
    /// <returns>A copy of the position on the first tick where <paramref name="arrived"/> was
    /// true, the same kind of copy <see cref="PositionOf"/> returns.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity"/> or
    /// <paramref name="arrived"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeoutTicks"/>
    /// is less than 1.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown when <paramref name="timeoutTicks"/>
    /// elapses without the position satisfying <paramref name="arrived"/>. The message is the one
    /// <see cref="Until"/> gives, and names <paramref name="timeoutTicks"/> and its value.</exception>
    /// <remarks>Runs on the game thread, <paramref name="arrived"/> included; a predicate that
    /// throws faults the returned task with that exception, as in <see cref="Until"/>, which this
    /// is a thin layer over. The position is read after the pass's own physics step, so it is the
    /// exact landing spot only for an entity that is grounded or settled: a creature that lands on
    /// water or falls on arrival has already moved on by the time the tick is observed. Test for
    /// the dimension or a distance, not for an exact block, when that matters. There is no
    /// <c>TeleportEntity</c> counterpart in Atlas: the engine can move a non-player entity within
    /// a dimension (<c>Entity.TeleportTo</c>), but only a player across dimensions, so a
    /// dimension change of another entity belongs to the mod under test, which this method
    /// then waits for.</remarks>
    Task<EntityPos> WaitForPosition(Entity entity, System.Func<EntityPos, bool> arrived, int timeoutTicks = Internal.Scheduling.TickBounds.DefaultWait);

    /// <summary>Runs a server command as the console (admin role, every privilege), e.g.
    /// <c>"/time set day"</c>, and returns its outcome.</summary>
    /// <param name="command">The command text, including the leading slash.</param>
    /// <returns>The command's outcome: success flag, resolved status message (the engine's, or
    /// Atlas's own sentence for a failure that came without one: see <see cref="CommandResult"/>),
    /// and the engine's raw <c>TextCommandResult</c> as an escape hatch.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> does not start
    /// with a slash: the engine's command dispatch strips the first character unconditionally, so
    /// a slashless command would be silently misparsed instead of failing loudly.</exception>
    /// <remarks>Runs on the game thread. Commands whose argument parsing goes async (e.g. player
    /// lookups) complete on a later tick; the returned task follows them to their final result.
    /// The engine reports such a command as <c>Deferred</c> first and calls back again once the
    /// handler has run; only that final callback completes the task, so the outcome is never
    /// <c>Deferred</c> (a hand-built helper that returns on the first callback can stop at it).
    /// The task has no tick bound of its own, so a handler that never calls back leaves it
    /// pending until the scenario watchdog cuts the scenario off. An unknown command completes
    /// with <c>Ok = false</c> rather than throwing, so scenarios can assert on intentional
    /// failures. The caller carries no player: a <c>RequiresPlayer</c> command refuses it (the
    /// remarks on <see cref="CommandResult"/> say how to recognise that refusal), and a
    /// reply the handler routes through <c>args.Caller.Player.SendMessage</c> has nowhere to
    /// land. Run it as a joined player instead with <see cref="ITestPlayer.ExecuteCommand"/>.</remarks>
    Task<CommandResult> ExecuteCommand(string command);

    /// <summary>Waits for a number of ticks to elapse.</summary>
    /// <param name="count">The number of ticks to wait. Must be at least 1.</param>
    /// <returns>A task that completes once <paramref name="count"/> ticks have elapsed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is less
    /// than 1.</exception>
    /// <remarks>Runs on the game thread. One tick is one fire of the engine's game-tick listener,
    /// at most one per <c>ServerMain.Process()</c> pass, which is about 33 ms at the engine's
    /// default pacing. It is not a promise of <paramref name="count"/> entity-simulation ticks:
    /// read <see cref="EntitySimulationTicks"/> when the simulation count itself matters.</remarks>
    Task Ticks(int count);

    /// <summary>Waits until a predicate becomes true, polled once per tick, or a timeout elapses.</summary>
    /// <param name="predicate">The condition to poll. First evaluated on the tick after the call,
    /// never before this method returns, so a condition already true at the call site still costs
    /// one tick.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait before giving up. Must be
    /// at least 1.</param>
    /// <returns>A task that completes when <paramref name="predicate"/> is true.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeoutTicks"/>
    /// is less than 1.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown when <paramref name="timeoutTicks"/> elapses
    /// without <paramref name="predicate"/> becoming true. The message names the bound that
    /// elapsed (<c>Until predicate still false after 600 ticks (timeoutTicks is 600; pass a larger
    /// value to wait longer)</c>, for the default); the scenario's own <c>TimeoutMs</c> is a
    /// separate limit and does not move it.</exception>
    /// <remarks><para>Runs on the game thread, <paramref name="predicate"/> included.</para>
    /// <para>To have the timeout message say what the wait was for, pass a description:
    /// <see cref="WorldSessionExtensions.Until(IWorldSession, Func{bool}, string, int)"/>, an
    /// extension method that is not a member of this interface, so it needs no implementation of
    /// its own.</para></remarks>
    // The default is the shared bound, not a literal, so it cannot drift from the waits Atlas
    // writes against it. A const default is baked into this signature's metadata as 600, so
    // nothing internal leaks into the public surface.
    Task Until(Func<bool> predicate, int timeoutTicks = Internal.Scheduling.TickBounds.DefaultWait);

    /// <summary>Runs <paramref name="count"/> ticks while measuring what the game thread did:
    /// per-pass busy time (min/median/p95/max plus the mean and the total, in milliseconds,
    /// excluding the engine's own pacing sleep), the number of passes actually sampled, total
    /// wall time, and game-thread allocations.</summary>
    /// <param name="count">The number of ticks to run and measure. Must be at least 1. Same
    /// semantics as <see cref="Ticks"/>: pacing is unchanged, this only observes it.</param>
    /// <returns>The measurement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is
    /// less than 1.</exception>
    /// <remarks><para>Runs on the game thread. What this measures: only work that happens
    /// inside <c>ServerMain.Process()</c> on Atlas's own game thread during the window - engine
    /// server systems, the game-tick event (which is where a mod's own
    /// <c>RegisterGameTickListener</c> handler runs), and anything a scenario's own code does
    /// inside a tick listener or command handler on this thread. It is a profiling tool built
    /// from a live running server, not an instrumenting profiler: it cannot attribute time or
    /// allocations to a specific mod, method or line, only to "this window of N ticks", and it
    /// cannot see work the engine does off the game thread (chunk generation, networking, the
    /// background assets build) or work a mod schedules onto the .NET thread pool.</para>
    /// <para>How noisy it is: busy time is read from the engine's own per-pass bookkeeping at
    /// whole-millisecond resolution (see <see cref="PassTimingStats"/>), so a fast, idle pass
    /// commonly reads as 0 ms; allocations are an exact per-thread count, but how much the
    /// engine itself allocates in each pass's own work varies run to run. Both are measured,
    /// with the spread this machine saw, in docs/specs/2026-09-23-tick-timing.md - read it
    /// before treating a single measurement as exact, and prefer comparing medians or p95s
    /// across repeated windows over trusting one window's numbers alone.</para>
    /// <para>Each joined test player adds a tick listener that only takes its packets out of the
    /// connection's receive buffer, dropping the kinds Atlas does not decode (see
    /// <see cref="IClientObservations"/>), and the host has one more that empties the UDP queue the
    /// test players share. They run inside the window and are not excluded. Measured on 1.21.7 and 1.22.3 with three players, the per-pass listener costs about 1 microsecond
    /// and allocates under 0.25 KB per pass, below the millisecond resolution of
    /// <see cref="TickMeasurement.BusyTime"/>, with no change in its median, its p95 or
    /// <see cref="TickMeasurement.AllocatedBytes"/> beyond noise.
    /// A read on <see cref="ITestPlayer.Client"/> made inside the window (for example in an
    /// <see cref="Until"/> predicate) decodes on the game thread and counts in
    /// <see cref="TickMeasurement.AllocatedBytes"/>, as it already did.</para></remarks>
    Task<TickMeasurement> MeasureTicks(int count);

    /// <summary>Saves the world now, the way an autosave does, and completes once the save has
    /// been written out. A scenario can check the save path of a mod (what it writes when the
    /// engine raises <c>GameWorldSave</c>, what it leaves in the savegame) without restarting
    /// the world to reach it.</summary>
    /// <returns>A task that completes when the engine has run the save and its background half
    /// has finished: the savegame blob, the dirty chunks and the map chunks are in the
    /// database.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the engine's save machinery is still
    /// busy after 5000 ticks (about 165 seconds at the default pacing), either before the save
    /// or after it, or when the engine refuses the save: its <c>/autosavenow</c> reports "not
    /// ready" and "backup in progress" as successes with other texts, and the message of this
    /// exception quotes the one it got.</exception>
    /// <remarks><para>Runs on the game thread. The engine runs the <c>GameWorldSave</c> handlers
    /// once, inline on the game thread, while the server is suspended, so they have all run when
    /// the task completes and no tick runs during them. Measured on 1.20.12, 1.21.7, 1.22.3 and
    /// 1.22.7: that pass blocks the game thread for 21 to 49 ms. A save that is already in flight
    /// (the engine's own timed autosave, or the background half of an earlier save) is waited
    /// out first, so the call never overlaps one and never skips.</para>
    /// <para>Nothing around the save is changed: the timed autosave stays on, the background chunk
    /// unloader keeps running, and no chunk is marked for saving that a real autosave would not
    /// save. This is deliberately not the capture <c>RollbackWorld</c> performs, which turns both
    /// background writers off for the rest of the class.</para>
    /// <para>The engine announces the save in chat: every joined player receives the line
    /// "Saving game world....", in <see cref="IClientObservations.Chat"/> and
    /// <see cref="IClientObservations.ChatLines"/>. Call <see cref="IClientObservations.Clear"/>
    /// before the save when the scenario then asserts on a player's chat, or the line is part
    /// of what it sees.</para></remarks>
    Task SaveNow();

    /// <summary>Joins a headless test player into the world. Multiple players can be joined into
    /// the same world, each under its own name.</summary>
    /// <param name="name">The player name to join as. The engine only accepts letters, digits,
    /// underscores and dashes, 16 characters at most; anything else is rejected by the server
    /// rather than by Atlas. Must also be unique within the world: the server identifies accounts
    /// by a name-derived UID, so a duplicate would be treated as the same account reconnecting
    /// and kick the first player.</param>
    /// <returns>The joined player, once its entity has spawned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="AtlasSetupException">Thrown when a test player with the same name is
    /// already joined in this world - including by an earlier scenario in the same class, since
    /// the class host (and its world) is shared by every scenario in the class. Also thrown when
    /// the server rejected the join (an invalid name, or a network-version drift relative to the
    /// Atlas build), when the client stayed registered but never reached the <c>Playing</c> state
    /// within the tick bound, and when the boot's background server-assets build had not settled
    /// within its own bound (1800 ticks, about 60 seconds at the default pacing) after the join.
    /// Those three messages name the server log directory.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown when the join's own inventory wait
    /// elapses: the player's inventories were not wired up within 100 ticks of the RequestJoin
    /// packet, which a mod-under-test stalling the engine's <c>OnPlayerJoin</c> can cause.</exception>
    /// <remarks><para>Runs on the game thread. The refusal of a <see langword="null"/> name and of a
    /// name that already joined throws from the call itself, before it returns a task; the rest
    /// of the join fails the task. Backed by the same dummy-network mechanism the game's
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
    /// reaches <c>Playing</c>, and the kick is observed via <c>ITestPlayer.IsConnected</c>.
    /// The join scatters the player up to the world's <c>spawnRadius</c> around the spawn, and the
    /// engine registers the entity in the chunk of that final position when it spawns it, so the
    /// returned player's <c>Entity.InChunkIndex3d</c> already matches where it stands.</para>
    /// <para>The player is on the highest-privilege role for the whole join: the engine puts a
    /// dummy-socket player back on that role when its role record is created, when it handles the
    /// join request and in its own <c>PlayerJoin</c> handler, all keyed on the same
    /// <c>IsSinglePlayerClient</c> check that also skips auth, wires the player to the dummy UDP
    /// server and exempts it from ping timeouts, so a record created before the join does not
    /// survive it and flipping that check would change much more than the role. To arrive on
    /// another role, join with <see cref="JoinPlayer(string, JoinOptions)"/> and
    /// <see cref="JoinOptions.Role"/>, which lowers the role in a <c>PlayerJoin</c> handler, after
    /// all three; the limits of doing so are written there. The role a player arrived with is
    /// read through <c>player.Player.Role</c> (<c>IServerPlayer.Role</c>).</para></remarks>
    Task<ITestPlayer> JoinPlayer(string name);

    /// <summary>Joins a headless test player into the world like <see cref="JoinPlayer(string)"/>,
    /// with a role to arrive on and whether it picks up items.</summary>
    /// <param name="name">The player name to join as, under the rules of
    /// <see cref="JoinPlayer(string)"/>: letters, digits, underscores and dashes, 16 characters at
    /// most, and unique within the world.</param>
    /// <param name="options">What the player is joined with; see <see cref="JoinOptions"/>. A
    /// <c>new JoinOptions()</c> joins exactly as <see cref="JoinPlayer(string)"/> does.</param>
    /// <returns>The joined player, once its entity has spawned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or
    /// <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <see cref="JoinOptions.Role"/> is not a role
    /// <c>IServerPlayer.SetRole</c> accepts, one of the server's configured roles; the message
    /// names the configured ones. Nothing was joined and the name is still free.</exception>
    /// <exception cref="AtlasSetupException">Thrown in every case <see cref="JoinPlayer(string)"/>
    /// throws it.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown in the case <see cref="JoinPlayer(string)"/>
    /// throws it.</exception>
    /// <remarks><para>Runs on the game thread. The join is the one <see cref="JoinPlayer(string)"/>
    /// runs, see its remarks, and so is the returned player. The checks of the arguments, and the
    /// refusal of a name that already joined, throw from the call itself, before it returns a
    /// task; everything the join does afterwards fails the task it returns.</para>
    /// <para><see cref="JoinOptions.Role"/> is applied by a <c>PlayerJoin</c> handler that this
    /// call subscribes before the join starts and removes when it returns or fails. The engine puts
    /// a dummy-socket player back on the highest-privilege role when its role record is created,
    /// when it handles the join request and in its own <c>PlayerJoin</c> handler, so the role
    /// cannot be fixed before the join; the handler lowers it after all three. From then on
    /// everything the server works out from the role comes from the requested one, starting with
    /// the privileges it sends the player and including what a <c>PlayerNowPlaying</c> handler
    /// reads. The packets sent before that point (level, assets, player entities) are not built
    /// from the role by the engine. Two limits, both the engine's:</para>
    /// <list type="bullet">
    /// <item><description>Handlers run in the order they were subscribed, and the handler this call
    /// adds comes after every <c>PlayerJoin</c> handler subscribed before it, a mod's own at boot
    /// included. Those still see the joiner on the highest-privilege role, so a mod that decides
    /// something from the joiner's role inside its join handler is not tested as a restricted
    /// join: only what it does later is.</description></item>
    /// <item><description>The role holds until the engine next reads the player's record, which puts
    /// a test player back on the highest-privilege role: a mod granting or revoking a privilege
    /// for it, a rejoin, and the commands listed in <see cref="ITestPlayer.ExecuteCommand"/>. A
    /// mod doing that from a <c>PlayerNowPlaying</c> handler makes the call return the player on
    /// the highest-privilege role. Read the role back with <c>player.Player.Role</c>
    /// (<c>IServerPlayer.Role</c>) when it matters, and use
    /// <see cref="TestPlayerExtensions.WithRole"/> for a role that is only wanted for a
    /// while.</description></item>
    /// </list>
    /// <para><see cref="JoinOptions.CollectItems"/> set to <see langword="false"/> turns the
    /// pickup of items on the ground off from the first moment the join sees the player's entity,
    /// before its <c>PlayerJoin</c> event. See <see cref="JoinOptions.CollectItems"/> for how and
    /// what it does not cover.</para></remarks>
    Task<ITestPlayer> JoinPlayer(string name, JoinOptions options);

    /// <summary>Gets a read-only stats view over any entity, for assertions.</summary>
    /// <param name="entity">The entity to read stats from.</param>
    /// <returns>The stats view.</returns>
    /// <remarks>Runs on the game thread. Works for any entity, not just players - e.g. a
    /// creature spawned via <see cref="SpawnEntity"/>.</remarks>
    IEntityStats StatsOf(Entity entity);
}
