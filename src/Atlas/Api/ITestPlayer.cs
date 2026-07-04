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

    /// <summary>Gets the player's current position.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    BlockPos Position { get; }

    /// <summary>Gets the player's stats (health, saturation, and generic attributes).</summary>
    /// <remarks>Runs on the game thread.</remarks>
    IEntityStats Stats { get; }

    /// <summary>Gives the player an item or block stack, placed into the active hotbar slot.</summary>
    /// <param name="itemOrBlockCode">The item's or block's asset location code, e.g.
    /// <c>"game:bread-spelt"</c> or <c>"game:soil-medium-normal"</c>.</param>
    /// <param name="quantity">The stack size to give.</param>
    /// <returns>A task that completes once the item has been given.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemOrBlockCode"/> does not
    /// resolve to a known item or block.</exception>
    /// <remarks>Runs on the game thread.</remarks>
    Task GiveItem(string itemOrBlockCode, int quantity = 1);

    /// <summary>Teleports the player to the given position, in that position's dimension.</summary>
    /// <param name="pos">The destination, including dimension.</param>
    /// <returns>A task that completes once the teleport has been applied.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    Task TeleportTo(BlockPos pos);
}
