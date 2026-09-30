using Vintagestory.API.Common;
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
    /// highlights per slot, particle spawns, mod-channel packets by type, chat lines); every
    /// other packet is dropped. See <see cref="IClientObservations"/> for the exclusive drain,
    /// accumulation and clearing rules.</summary>
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
    /// entity's dimension and its coordinates match <paramref name="pos"/>, and the entity is
    /// present in the target chunk. The underlying engine call defers the coordinate move until
    /// the target chunk is loaded, so completion is chunk-load-dependent and is not instant even
    /// though it usually resolves within a tick or two for already-loaded terrain.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when the teleport does not finish
    /// applying within the internal tick bound (600 ticks), most likely because the target
    /// chunk never finished loading.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Task TeleportTo(BlockPos pos);

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
    /// <returns>The command's outcome: success flag, resolved status message, and the engine's
    /// raw <c>TextCommandResult</c> as an escape hatch.</returns>
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
    /// the same way. That downgrade is not sticky: every later call of
    /// <c>GetOrCreateServerPlayerData</c> for this player's uid puts it straight back on the
    /// highest-privilege role, silently undoing the downgrade. The engine makes that call when
    /// the player rejoins, when a mod grants, revokes or denies a privilege for it, or removes a
    /// denial, through <c>IPermissionManager</c>, and in these commands: <c>/self role</c>,
    /// <c>/group create</c> and, with this player as the target, <c>/op</c>,
    /// <c>/player &lt;name&gt; role</c>,
    /// <c>/player &lt;name&gt; privilege grant|revoke|deny|removedeny</c>,
    /// <c>/player &lt;name&gt; whitelist add|remove</c> and <c>/whitelist add|remove</c> (the
    /// latter only once it goes on to change the list). The commands that set a role,
    /// <c>/op</c> (which names <c>admin</c>) and <c>/player &lt;name&gt; role &lt;code&gt;</c>,
    /// fetch first and then assign the role they name, as <c>SetRole</c> itself does, so they end
    /// on that role: run from the console, <c>/player &lt;name&gt; role suplayer</c> is another
    /// way to downgrade. Only the query form <c>/player &lt;name&gt; role</c>, with no role, and a
    /// change the engine refuses (the restored role is the one named, or the caller targets
    /// itself) leave the player on the highest-privilege role. <c>/player &lt;name&gt; wipedata</c>
    /// does not fetch, but it deletes the player's record, which the next call rebuilds on the
    /// highest-privilege role. The other <c>/player</c> subcommands, and a command aimed at
    /// another player, leave the role alone. A downgraded player reaches the handler of
    /// <c>/self role</c> (which then reports the restored role) and of <c>/group create</c>; the
    /// rest need <c>grantrevoke</c> or <c>whitelist</c>, which the stock roles below admin lack,
    /// so they are refused first and change nothing.</para>
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
