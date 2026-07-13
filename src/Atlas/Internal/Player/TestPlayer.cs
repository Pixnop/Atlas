using Atlas.Api;
using Atlas.Internal.Scheduling;
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
    private readonly ServerMain _server;
    private readonly ConnectedClient _client;
    private readonly TickSource _ticks;

    /// <summary>Initializes a new instance of the <see cref="TestPlayer"/> class.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="server">The live server, for the <see cref="IsConnected"/> registry check.</param>
    /// <param name="client">The connected client backing this player.</param>
    /// <param name="ticks">The tick source used to bound the wait for a teleport's deferred
    /// chunk-load-dependent application.</param>
    public TestPlayer(ICoreServerAPI api, ServerMain server, ConnectedClient client, TickSource ticks)
    {
        _api = api;
        _server = server;
        _client = client;
        _ticks = ticks;
    }

    /// <inheritdoc/>
    public bool IsConnected
        => _server.Clients.TryGetValue(_client.Id, out ConnectedClient? registered)
            && ReferenceEquals(registered, _client);

    /// <inheritdoc/>
    public EntityPlayer Entity => _client.Entityplayer;

    /// <inheritdoc/>
    public IServerPlayer Player => _client.Player;

    /// <inheritdoc/>
    /// <remarks>Read through <c>Entity.SidedPos</c> (ServerPos on the server), never
    /// <c>Entity.Pos</c>: pre-1.22 engines keep Pos and ServerPos as two separate instances and
    /// the join path only maintains ServerPos for a headless player, leaving Pos at the origin;
    /// on 1.22 the three names are one instance, so this reads identically there. CS0618: 1.22
    /// marks the old names obsolete, but they are exactly the pre-1.22 compatibility surface.</remarks>
#pragma warning disable CS0618 // SidedPos exists on every supported version; obsolete on 1.22 only.
    public BlockPos Position => Entity.SidedPos.AsBlockPos;
#pragma warning restore CS0618

    /// <inheritdoc/>
    public IEntityStats Stats => new EntityStatsView(Entity);

    /// <inheritdoc/>
    public Task GiveItem(string itemOrBlockCode, int quantity = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        var location = new AssetLocation(itemOrBlockCode);
        ItemStack stack = ResolveStack(nameof(itemOrBlockCode), location, quantity);

        // Cap check happens after resolving the stack, since MaxStackSize is a property of the
        // resolved collectible, not of the raw code/quantity pair.
        int maxStackSize = stack.Collectible.MaxStackSize;
        if (quantity > maxStackSize)
        {
            string message = $"'{itemOrBlockCode}' has a max stack size of {maxStackSize}; {quantity} " +
                "exceeds it. Give it in multiple calls/slots instead of one oversized stack.";
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, message);
        }

        IPlayerInventoryManager inventory = Player.InventoryManager;
        int slotNumber = inventory.ActiveHotbarSlotNumber;
        IInventory hotbar = inventory.GetHotbarInventory();
        ItemSlot slot = hotbar[slotNumber];
        slot.Itemstack = stack;
        slot.MarkDirty();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task TeleportTo(BlockPos pos)
    {
        // Ordering note: EntityPlayer.ChangeDimension applies immediately (Pos.Dimension is
        // written synchronously, plus dimension-changed bookkeeping), while
        // Entity.TeleportTo(EntityPos, Action)/TeleportToDouble defer the actual coordinate move
        // until the target chunk is loaded - so between these two calls there is necessarily a
        // window where the dimension has changed but the coordinates have not caught up yet.
        // That window is internal to this method: the returned task only completes once BOTH the
        // dimension change and the position callback have landed, so no caller ever observes the
        // intermediate state. The check reads SidedPos (the server-authoritative instance on
        // every supported version; obsolete-as-alias on 1.22 only, hence the pragma);
        // ChangeDimension writes both Pos and ServerPos dimensions on pre-1.22 engines, so the
        // check stays in sync with what it triggers.
#pragma warning disable CS0618 // SidedPos exists on every supported version; obsolete on 1.22 only.
        if (Entity.SidedPos.Dimension != pos.dimension)
#pragma warning restore CS0618
        {
            Entity.ChangeDimension(pos.dimension);
        }

        var target = new EntityPos();
        target.SetPos(pos);
        target.Dimension = pos.dimension;

        // The callback runs on the game thread once the target chunk is loaded and the move is
        // actually applied; bridge it to the awaitable TickSource/Until machinery via a volatile
        // flag instead of the callback's own thread, since the callback fires from inside the
        // engine's chunk-load completion, not from arbitrary code.
        bool applied = false;
        Entity.TeleportTo(target, () => Volatile.Write(ref applied, true));

        try
        {
            await _ticks.WaitUntilAsync(() => Volatile.Read(ref applied), timeoutTicks: 600).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new ScenarioTimeoutException(
                $"TeleportTo({pos}) did not apply within {ex.TicksWaited} ticks: the target chunk " +
                "never finished loading (or its onTeleported callback never fired).",
                ex.TicksWaited);
        }
    }

    /// <summary>Resolves an item or block code to a one-stack <see cref="ItemStack"/>.</summary>
    /// <param name="publicParameterName">The public API parameter name to attribute an unresolved
    /// code to, so the exception points callers at <see cref="GiveItem"/>'s own signature instead
    /// of this private helper's argument name.</param>
    /// <param name="location">The asset location to resolve.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>The resolved stack.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="location"/> resolves to
    /// neither a known item nor a known block.</exception>
    private ItemStack ResolveStack(string publicParameterName, AssetLocation location, int quantity)
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

        throw new ArgumentException($"Unknown item or block code '{location}'", publicParameterName);
    }
}
