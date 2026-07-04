using Atlas.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Internal.Player;

/// <summary>A headless player joined via <see cref="DummyClientConnector"/>, wrapping the
/// resulting <see cref="ConnectedClient"/>/<see cref="EntityPlayer"/> pair as <see cref="ITestPlayer"/>.</summary>
internal sealed class TestPlayer : ITestPlayer
{
    private readonly ICoreServerAPI _api;
    private readonly ConnectedClient _client;

    /// <summary>Initializes a new instance of the <see cref="TestPlayer"/> class.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="client">The connected client backing this player.</param>
    public TestPlayer(ICoreServerAPI api, ConnectedClient client)
    {
        _api = api;
        _client = client;
    }

    /// <inheritdoc/>
    public EntityPlayer Entity => _client.Entityplayer;

    /// <inheritdoc/>
    public IServerPlayer Player => _client.Player;

    /// <inheritdoc/>
    public BlockPos Position => Entity.Pos.AsBlockPos;

    /// <inheritdoc/>
    public IEntityStats Stats => new EntityStatsView(Entity);

    /// <inheritdoc/>
    public Task GiveItem(string itemOrBlockCode, int quantity = 1)
    {
        var location = new AssetLocation(itemOrBlockCode);
        ItemStack stack = ResolveStack(location, quantity);

        IPlayerInventoryManager inventory = Player.InventoryManager;
        int slotNumber = inventory.ActiveHotbarSlotNumber;
        IInventory hotbar = inventory.GetHotbarInventory();
        ItemSlot slot = hotbar[slotNumber];
        slot.Itemstack = stack;
        slot.MarkDirty();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task TeleportTo(BlockPos pos)
    {
        if (Entity.Pos.Dimension != pos.dimension)
        {
            // Entity.TeleportTo(BlockPos)/TeleportToDouble never read or apply pos.dimension (the
            // same gotcha WorldSession.SpawnEntity works around) - ChangeDimension is the
            // purpose-built API that both updates Pos.Dimension and does the bookkeeping a raw
            // field write would skip (dimension-changed event, chunk tracking).
            Entity.ChangeDimension(pos.dimension);
        }

        Entity.TeleportTo(pos);
        return Task.CompletedTask;
    }

    /// <summary>Resolves an item or block code to a one-stack <see cref="ItemStack"/>.</summary>
    /// <param name="location">The asset location to resolve.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>The resolved stack.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="location"/> resolves to
    /// neither a known item nor a known block.</exception>
    private ItemStack ResolveStack(AssetLocation location, int quantity)
    {
        Item? item = _api.World.GetItem(location);
        if (item != null)
        {
            return new ItemStack(item, quantity);
        }

        Block? block = _api.World.GetBlock(location);
        if (block != null && !block.IsMissing)
        {
            return new ItemStack(block, quantity);
        }

        throw new ArgumentException($"Unknown item or block code '{location}'", nameof(location));
    }
}
