using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Api;

/// <summary>A headless player joined into the test world: no rendering, no real network.</summary>
/// <remarks>Backed by the same dummy-network mechanism the game's own singleplayer client uses
/// to talk to its local server, so the resulting player is a real, world-present
/// <see cref="EntityPlayer"/> with inventory, health, and every other behavior an ordinary
/// connected player has. Every member runs on the game thread.</remarks>
public interface ITestPlayer
{
    /// <summary>Gets the player's live entity. Escape hatch for anything not covered by this
    /// surface.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    EntityPlayer Entity { get; }

    /// <summary>Gets the player's live server-side player object. Escape hatch for anything not
    /// covered by this surface.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    IServerPlayer Player { get; }

    /// <summary>Gets a value indicating whether the player is still connected to the server.</summary>
    /// <value><see langword="false"/> once the server has dropped the player: a mod-under-test
    /// kicking it (<c>IServerPlayer.Disconnect</c>), a ban, or any other server-side removal.
    /// Test players never leave on their own, so a <see langword="false"/> value always means
    /// the server ended the connection.</value>
    /// <remarks>Runs on the game thread. May stay <see langword="true"/> for a few ticks after
    /// the kick: mods that kick from a background thread (a common pattern, e.g. after an HTTP
    /// check inside a PlayerJoin handler) crash the engine's own teardown halfway, and Atlas
    /// finishes that teardown on the game thread a couple of ticks later; this property reports
    /// the settled truth, not the in-flight state. Wait with
    /// <c>await World.Until(() =&gt; !player.IsConnected)</c> rather than asserting immediately
    /// after the kick.</remarks>
    bool IsConnected { get; }

    /// <summary>Gets the player's current position.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    BlockPos Position { get; }

    /// <summary>Gets the player's stats (health, saturation, and generic attributes).</summary>
    /// <remarks>Runs on the game thread.</remarks>
    IEntityStats Stats { get; }

    /// <summary>Gets the subset of what the server sent to this player that Atlas decodes (block
    /// highlights per slot, particle spawns, mod-channel packets by type, chat lines, entity
    /// arrivals and departures, player world data, player-group listings); every other packet is
    /// dropped as it arrives, without being decoded. See <see cref="IClientObservations"/> for the
    /// per-pass drain, the arrival ticks, and the accumulation and clearing rules.</summary>
    /// <remarks>Runs on the game thread. Captured from the player's own connection, so each
    /// player observes exactly its own traffic.</remarks>
    IClientObservations Client { get; }

    /// <summary>Gives the player an item or block stack, placed into the active hotbar slot.</summary>
    /// <param name="itemOrBlockCode">The item's or block's asset location code, e.g.
    /// <c>"game:flint"</c> or <c>"game:soil-medium-normal"</c>.</param>
    /// <param name="quantity">The stack size to give. Must be at least 1 and no more than the
    /// resolved item's or block's <c>MaxStackSize</c>.</param>
    /// <returns>A task that completes once the item has been given.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemOrBlockCode"/> does not
    /// resolve to a known item or block.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="quantity"/> is
    /// less than 1, or greater than the resolved collectible's <c>MaxStackSize</c>.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Task GiveItem(string itemOrBlockCode, int quantity = 1);

    /// <summary>Teleports the player to the given position, in that position's dimension.</summary>
    /// <param name="pos">The destination, including dimension.</param>
    /// <returns>A task that completes once the teleport has actually been applied: both the
    /// entity's dimension and its coordinates match <paramref name="pos"/>, and the engine has the
    /// entity in the target chunk: <c>Entity.InChunkIndex3d</c> is the index of that chunk, and
    /// the chunk's entity list holds the entity (so <see cref="IWorldSession.EntitiesIn(WorldArea)"/>
    /// finds it there). The underlying engine call defers the coordinate move until the target
    /// chunk is loaded, so completion is chunk-load-dependent and is not instant even though it
    /// usually resolves within a tick or two for already-loaded terrain.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when the teleport does not finish
    /// applying within the internal tick bound (600 ticks), most likely because the target
    /// chunk never finished loading.</exception>
    /// <remarks><para>Runs on the game thread.</para>
    /// <para>The engine alone would leave the entity registered in the chunk it came from for up
    /// to a second after the teleport (its once-a-second entity pass, 20 to 30 ticks measured),
    /// and everything it centres on the player's entity chunk reads that registration:
    /// random-tick candidates around the player, for one, so a block placed right after the
    /// teleport would get no random ticks until the pass ran. Atlas registers the entity in the
    /// target chunk itself, through the engine's <c>UpdateEntityChunk</c>, before the task
    /// completes. That costs no tick and no wait. It cannot happen when no chunk exists at the
    /// destination (a position above or below the world, or in a mini-dimension chunk nothing
    /// created): the engine has nothing to register the entity in, the index stays as it was and
    /// the task completes all the same.</para>
    /// <para>The move skips the terrain collision pass, so <c>Block.OnEntityCollide</c> does not
    /// fire for a teleported player, wherever it lands, while <c>Block.OnEntityInside</c> fires
    /// on every tick the player's box overlaps the block, even a block with no collision box.
    /// Assert that the callback fired rather than an exact count. <see cref="WalkTo"/> moves the
    /// player through that collision pass.</para></remarks>
    Task TeleportTo(BlockPos pos);

    /// <summary>Walks the player in a straight line to the middle of a block, through the same
    /// collision pass the server runs for a player that walks: <c>Block.OnEntityCollide</c> fires
    /// for what a step runs into, and the walk stops there.</summary>
    /// <param name="pos">The destination block, in the dimension the player is in. The player
    /// ends in the middle of it horizontally, with its feet at <c>pos.Y</c>, which is where
    /// <see cref="TeleportTo"/> would put them vertically.</param>
    /// <returns>A task that completes one tick after the last step, with a copy of the position
    /// the player reached: the middle of <paramref name="pos"/> when nothing was in the way, and
    /// the step before the blockage when something was. A blockage is not an error. Compare the
    /// result with the destination to tell the two apart.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pos"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="pos"/> is in another
    /// dimension than the player: a walk does not cross dimensions, <see cref="TeleportTo"/>
    /// does.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the player is mounted (it
    /// has to leave its seat first), or has no remote-physics behavior for the server to hand a
    /// step to.</exception>
    /// <remarks><para>Runs on the game thread, one step per tick, and the call itself takes the
    /// first step. A step is 0.2 blocks along the straight line to the destination (6 blocks a
    /// second at the engine's 30 passes a second), so a walk of <c>n</c> blocks takes about
    /// <c>5n</c> ticks. The task has no tick bound of its own: it ends when the walk does.</para>
    /// <para>Each step moves the entity and hands the move to the player's remote-physics
    /// behavior (<c>IRemotePhysics.HandleRemotePhysics</c>), which is what the engine calls for
    /// every position packet a real client sends. Its collision pass calls
    /// <c>Block.OnEntityCollide</c> on the block a step runs into and sets
    /// <c>Entity.CollidedHorizontally</c>, which is what stops the walk: the player stays at the
    /// step before, up to 0.2 blocks short of the obstacle, and the task completes. The blocked
    /// step's callback has fired by then, once or twice for the block it met first (the engine's
    /// pass tests twice), so assert that it fired rather than an exact count. A walk that starts
    /// blocked returns the starting position and fires the callback again.
    /// <c>Block.OnEntityInside</c> is called on every tick the player's box overlaps a block, as
    /// for a teleported player, including a block with no collision box, which the walk goes
    /// through. The task completes one tick after the last step so that those per-tick callbacks
    /// have seen the player where it stopped; a destination the player already stands on
    /// completes at once.</para>
    /// <para>It is a straight line and nothing smarter: the walk does not step up onto a block,
    /// slide along a wall or find a way around one, applies no gravity, and does not push the
    /// player out of a block it starts inside. A step that touches the floor is not a blockage.
    /// Walk between positions at the same height, on a path that is clear at that height.</para>
    /// <para>The engine's own pass keeps the player's chunk registration current only once a
    /// second, so the walk registers the entity in the chunk each step ends in, as
    /// <see cref="TeleportTo"/> does, and the player is in the right chunk at every tick. The
    /// task ends with the player standing still, not carrying the last step's
    /// velocity.</para></remarks>
    Task<EntityPos> WalkTo(BlockPos pos);

    /// <summary>Sets what the player looks at, as the server sees it: the block selection that
    /// <see cref="IPlayer.CurrentBlockSelection"/> returns and that commands, items and
    /// block callbacks read for "the block the player is aiming at".</summary>
    /// <param name="pos">The block to look at.</param>
    /// <param name="face">The face of it, <see cref="BlockFacing.UP"/> when <see langword="null"/>.
    /// The hit point is the middle of that face.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pos"/> is
    /// <see langword="null"/>.</exception>
    /// <remarks><para>Runs on the game thread and takes effect at once: a command run with
    /// <see cref="ExecuteCommand"/> straight after sees the selection. The player does not
    /// move, but it turns (its yaw and pitch change), because of how the server keeps the
    /// selection.</para>
    /// <para>The server does not keep a selection that was written into the entity. Every tick it
    /// traces the selection again from the player's eye, along its yaw and pitch, as far as the
    /// player's picking range, and replaces whatever was there: a selection set by hand reads
    /// <see langword="null"/> one tick later (measured on 1.21.7, 1.22.3 and 1.22.7). So this
    /// member also aims the player at the middle of the face from where it stands, which is
    /// what a real player does, and the selection then holds across ticks as long as the block is
    /// in reach (<c>IPlayer.WorldData.PickingRange</c>, 100 blocks for the creative players of
    /// the default world) and in sight. The face the server reports from then on is the face the
    /// line of sight meets, so ask for one the player can see: <see cref="BlockFacing.UP"/> for a
    /// block lower than its eyes. When the block is out of reach or hidden behind another, the
    /// selection set here lasts until the next tick and the server's own trace replaces it, so
    /// read it, or run the command that reads it, before the next <c>await</c> of a tick, or
    /// move the player first. Moving the player afterwards, with <see cref="TeleportTo"/> or
    /// <see cref="WalkTo"/>, leaves it aimed at the old spot: call this again.</para></remarks>
    void LookAt(BlockPos pos, BlockFacing? face = null);

    /// <summary>Sends a chat line as the client would: a leading <c>/</c> runs a command through
    /// the server's normal chat path (privileges, rate limiting, and all), so a handler's reply
    /// arrives in <see cref="IClientObservations.ChatLines"/> exactly as it would for a real
    /// player; a plain line goes through the same broadcast/echo path a typed message does.</summary>
    /// <param name="message">The chat line text, unmodified (not required to start with a
    /// slash).</param>
    /// <returns>A task that completes once the server has both taken the packet off the
    /// connection and dispatched it: the wait polls the engine's off-thread packet parser until
    /// nothing is left pending on the connection (once per tick, bounded at 100 ticks), then
    /// waits two further ticks for the game thread's dispatch pass. A reply produced by that
    /// pass is readable through <see cref="Client"/> as soon as this task completes, with no
    /// further wait.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown when the packet is still not parsed off
    /// the connection after 100 ticks; the message says so, since that points at the embedded
    /// server being stuck rather than at the wait being too short.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Task Say(string message);

    /// <summary>Runs a server command with this player as the caller: the player's real role
    /// and privileges (not an admin stand-in), its position and entity, and returns its
    /// outcome.</summary>
    /// <param name="command">The command text, including the leading slash.</param>
    /// <returns>The command's outcome: success flag, resolved status message (the engine's, or
    /// Atlas's own sentence for a failure that came without one: see <see cref="CommandResult"/>),
    /// and the engine's raw <c>TextCommandResult</c> as an escape hatch.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> does not start
    /// with a slash: the engine's command dispatch strips the first character unconditionally, so
    /// a slashless command would be silently misparsed instead of failing loudly.</exception>
    /// <remarks><para>Runs on the game thread. Commands whose argument parsing goes async (e.g.
    /// player lookups) complete on a later tick; the returned task follows them to their final
    /// result. The engine reports such a command as <c>Deferred</c> first and calls back again
    /// once the handler has run; only that final callback completes the task, so the outcome is
    /// never <c>Deferred</c> (a hand-built helper that returns on the first callback can stop at
    /// it). The task has no tick bound of its own, so a handler that never calls back leaves it
    /// pending until the scenario watchdog cuts the scenario off. An unknown command
    /// completes with <c>Ok = false</c> rather than throwing, so scenarios can assert on
    /// intentional failures.</para>
    /// <para>A joined test player is admin by default: it rides the same dummy-socket path real
    /// singleplayer does, and the engine hands every such connection its highest-privilege role
    /// (<c>IsSinglePlayerClient</c>, in <c>PlayerDataManager.GetOrCreateServerPlayerData</c>),
    /// independent of the server's own configured default role. A test that wants to see a real
    /// refusal downgrades first, with <c>player.Player.SetRole("suplayer")</c>, then restores it
    /// the same way; <see cref="TestPlayerExtensions.WithRole"/> does both and has the same
    /// limit, and <see cref="JoinOptions.Role"/> joins a player already downgraded. That downgrade
    /// is not sticky: every later call of
    /// <c>GetOrCreateServerPlayerData</c> for this player's uid puts it straight back on the
    /// highest-privilege role, silently undoing the downgrade. The engine makes that call in these
    /// cases:</para>
    /// <list type="bullet">
    /// <item><description>the player rejoins;</description></item>
    /// <item><description>a mod grants, revokes or denies a privilege for the player, or removes a
    /// denial, through <c>IPermissionManager</c>;</description></item>
    /// <item><description><c>/self role</c>;</description></item>
    /// <item><description><c>/group create</c>;</description></item>
    /// <item><description><c>/op</c> with this player as the target;</description></item>
    /// <item><description><c>/player &lt;name&gt; role</c> with this player as the target;</description></item>
    /// <item><description><c>/player &lt;name&gt; privilege grant|revoke|deny|removedeny</c> with
    /// this player as the target;</description></item>
    /// <item><description><c>/player &lt;name&gt; whitelist add|remove</c> with this player as the
    /// target;</description></item>
    /// <item><description><c>/whitelist add|remove</c> with this player as the target, only once it
    /// goes on to change the list.</description></item>
    /// </list>
    /// <para>The commands that set a role, <c>/op</c> (which names <c>admin</c>) and
    /// <c>/player &lt;name&gt; role &lt;code&gt;</c>, fetch first and then assign the role they
    /// name, as <c>SetRole</c> itself does, so they end on that role: run from the console,
    /// <c>/player &lt;name&gt; role suplayer</c> is another way to downgrade. Only the query form
    /// <c>/player &lt;name&gt; role</c>, with no role, and a change the engine refuses (for example
    /// the player is already on the named role, the caller targets itself, or a player caller's own
    /// role is too low for the change) leave the player on the highest-privilege role.
    /// <c>/player &lt;name&gt; wipedata</c> does not fetch, but it drops the record the live player
    /// still holds: the player keeps its current role until it rejoins, and any later fetch,
    /// <c>SetRole</c> included, creates and changes a new record the live player does not read. The
    /// other <c>/player</c> subcommands, and a command aimed at another player, leave the role
    /// alone. A downgraded player reaches the handler of <c>/self role</c> (which then reports the
    /// restored role) and of <c>/group create</c>; the rest need <c>grantrevoke</c> or
    /// <c>whitelist</c>, which the stock roles below admin lack, so they are refused first and
    /// change nothing.</para>
    /// <para>How this differs from the other two ways to run a command: <see cref="Say"/> sends
    /// the exact packet a client's chat box builds, over this player's own dummy connection,
    /// bounded at 100 ticks for the send itself, going through rate limiting and the server's
    /// normal chat path, with any reply landing in <see cref="IClientObservations.ChatLines"/>
    /// rather than a returned value. This member skips that network round trip and calls the
    /// engine's command dispatch directly, the same way <see cref="IWorldSession.ExecuteCommand"/>
    /// does, but with this player behind it instead of a synthetic console caller that carries no
    /// player, so a <c>RequiresPlayer</c> command accepts it, a privilege check sees this
    /// player's real grants, and a reply routed through <c>args.Caller.Player.SendMessage</c> has
    /// somewhere to land (though it is not captured here; read it back through
    /// <see cref="Client"/> if the command sends one, the same as <see cref="Say"/>).</para>
    /// <para>Unlike the engine's own chat path (<c>ChatCommandApi.Execute(string, IServerPlayer,
    /// ...)</c>), the returned result's message is never sent to the player's own chat, and a
    /// handler exception surfaces as a faulted task rather than an <c>"exception"</c> error
    /// result; both differences also hold for the existing console path. Also unchanged from
    /// <see cref="Say"/>: called after <see cref="IsConnected"/> has already gone false, this
    /// still runs the command against the stale <see cref="IServerPlayer"/>, with no guard
    /// against it.</para></remarks>
    Task<CommandResult> ExecuteCommand(string command);
}
