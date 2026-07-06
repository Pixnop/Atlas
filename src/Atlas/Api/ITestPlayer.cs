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
    /// <value><see langword="false"/> once the server has dropped the player - a mod-under-test
    /// kicking it (<c>IServerPlayer.Disconnect</c>), a ban, or any other server-side removal.
    /// Test players never leave on their own, so a <see langword="false"/> value always means
    /// the server ended the connection.</value>
    /// <remarks>Runs on the game thread. May stay <see langword="true"/> for a few ticks after
    /// the kick: mods that kick from a background thread (a common pattern - e.g. after an HTTP
    /// check inside a PlayerJoin handler) crash the engine's own teardown halfway, and Atlas
    /// finishes that teardown on the game thread a couple of ticks later; this property reports
    /// the settled truth, not the in-flight state. Wait with
    /// <c>await world.Until(() =&gt; !player.IsConnected)</c> rather than asserting immediately
    /// after the kick.</remarks>
    bool IsConnected { get; }

    /// <summary>Gets the player's current position.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    BlockPos Position { get; }

    /// <summary>Gets the player's stats (health, saturation, and generic attributes).</summary>
    /// <remarks>Runs on the game thread.</remarks>
    IEntityStats Stats { get; }

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
    /// applying within the internal tick bound (600 ticks) - most likely because the target
    /// chunk never finished loading.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Task TeleportTo(BlockPos pos);
}
