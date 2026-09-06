using Atlas.Api;
using Atlas.Internal.Bootstrap;
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
    private readonly DummyPlayerConnection _connection;

    /// <summary>Initializes a new instance of the <see cref="TestPlayer"/> class.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="server">The live server, for the <see cref="IsConnected"/> registry check.</param>
    /// <param name="client">The connected client backing this player.</param>
    /// <param name="ticks">The tick source used to bound the wait for a teleport's deferred
    /// chunk-load-dependent application, and to give <see cref="Say"/>'s packet time to reach
    /// the server.</param>
    /// <param name="connection">The player's dummy connection, whose client side receives
    /// everything the server sends this player (see <see cref="ClientObservations"/>) and whose
    /// server side <see cref="Say"/> sends chat packets over.</param>
    public TestPlayer(ICoreServerAPI api, ServerMain server, ConnectedClient client, TickSource ticks, DummyPlayerConnection connection)
    {
        _api = api;
        _server = server;
        _client = client;
        _ticks = ticks;
        _connection = connection;
        Client = new ClientObservations(api, connection.TcpClient);
    }

    /// <inheritdoc/>
    public IClientObservations Client { get; }

    /// <inheritdoc/>
    public bool IsConnected => DummyClientConnector.IsRegistered(_server, _client);

    /// <inheritdoc/>
    public EntityPlayer Entity => _client.Entityplayer;

    /// <inheritdoc/>
    public IServerPlayer Player => _client.Player;

    /// <inheritdoc/>
    /// <remarks>Read through the sided position (ServerPos on the server), never
    /// <c>Entity.Pos</c>: pre-1.22 engines keep Pos and ServerPos as two separate instances and
    /// the join path only maintains ServerPos for a headless player, leaving Pos at the origin;
    /// on 1.22 the three names are one instance, so this reads identically there. See
    /// <see cref="EngineCompat.SidedPosOf"/> for why that read has one owner.</remarks>
    public BlockPos Position => EngineCompat.SidedPosOf(Entity).AsBlockPos;

    /// <inheritdoc/>
    public IEntityStats Stats => new EntityStatsView(Entity);

    /// <inheritdoc/>
    public Task GiveItem(string itemOrBlockCode, int quantity = 1)
    {
        ItemStack stack = ResolveStack(_api.World.GetItem, _api.World.GetBlock, itemOrBlockCode, quantity);

        IPlayerInventoryManager inventory = Player.InventoryManager;
        int slotNumber = inventory.ActiveHotbarSlotNumber;
        IInventory hotbar = inventory.GetHotbarInventory();
        ItemSlot slot = hotbar[slotNumber]
            ?? throw new InvalidOperationException(
                $"Hotbar slot {slotNumber} does not exist on '{Player.PlayerName}': the engine " +
                "returned no slot for the active hotbar index (1.22.4+ annotates the inventory " +
                "indexer as nullable).");
        slot.Itemstack = stack;
        slot.MarkDirty();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>Sends packet 4 over the player's own dummy connection
    /// (<see cref="DummyClientConnector.Say"/>), then waits for two hops the send itself does
    /// not cover before returning. Hop 1: the engine's <c>clientPacketsParser</c> background
    /// thread, decoupled from the game thread, polls the dummy socket every 10ms
    /// (<c>ClientPacketParserOffthread</c>, verified by decompile) and only then queues the
    /// parsed packet for dispatch - a genuine cross-thread race whose latency is wall-clock
    /// bounded, not tick-count bounded (a fixed-tick wait here was measured flaky under a loaded
    /// test run), so this polls <see cref="EngineCompat.PendingInboundCount"/> down to zero
    /// instead, bounded by <see cref="TickBounds.EngineHandshake"/> like Atlas's other waits on
    /// the engine's own machinery. Hop 2: dispatch itself - the game thread's NEXT
    /// <c>ServerMain.Process()</c> pass draining that queue and calling <c>HandleChatLine</c>,
    /// synchronously producing whatever reply the server sends back (a command's reply, or the
    /// engine's own echo of a plain line to its sender) - is purely game-thread-side and so IS
    /// reliably tick-bounded; this waits a fixed 2 ticks for it (1 is chronologically sufficient
    /// at the engine's default ~33ms pace, 2 is margin for a slow pass). A reply produced by
    /// that pass is already sitting in the connection's receive buffer by the time this method's
    /// continuation resumes (game-thread pump order: <c>Process()</c>, then the scheduler drain
    /// - see docs/specs/2026-07-14-tick-contract.md), so a caller reading <see cref="Client"/>
    /// right after this returns observes it with no further wait.</remarks>
    public async Task Say(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        DummyClientConnector.Say(_connection, message);

        try
        {
            await _ticks.WaitUntilAsync(
                () => EngineCompat.PendingInboundCount(_connection.TcpServer) == 0,
                timeoutTicks: TickBounds.EngineHandshake).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new ScenarioTimeoutException(
                $"Say({message.Length} chars) was not parsed off the connection within {ex.TicksWaited} " +
                "ticks: the engine's own background packet-parsing thread never caught up, which points " +
                "at the embedded server itself being stuck rather than this wait being too short.",
                ex.TicksWaited);
        }

        await _ticks.WaitTicksAsync(2).ConfigureAwait(true);
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
        // intermediate state. The check reads the sided position, the server-authoritative
        // instance on every supported version, while ChangeDimension writes both the Pos and the
        // ServerPos dimension on pre-1.22 engines, so the check stays in sync with what it
        // triggers.
        if (EngineCompat.SidedPosOf(Entity).Dimension != pos.dimension)
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
            await _ticks.WaitUntilAsync(
                () => Volatile.Read(ref applied), timeoutTicks: TickBounds.DefaultWait).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new ScenarioTimeoutException(
                $"TeleportTo({pos}) did not apply within {ex.TicksWaited} ticks: the target chunk " +
                "never finished loading (or its onTeleported callback never fired).",
                ex.TicksWaited);
        }
    }

    /// <summary>Every rule <see cref="GiveItem"/> applies before it touches an inventory: the
    /// quantity floor, the item-then-block lookup order, and the max-stack cap of whichever
    /// collectible the code resolved to. The world is reached through the two lookup delegates,
    /// so the rules and their three failure messages are checkable without a booted server (the
    /// <see cref="Internal.Hosting.ScratchCleanup"/> pure-core pattern); the parameter names are
    /// <see cref="GiveItem"/>'s own, because they are what the thrown
    /// <see cref="ArgumentException.ParamName"/> reports to a caller.</summary>
    /// <param name="getItem">Looks an asset location up in the item registry. Fully qualified,
    /// like <paramref name="getBlock"/>: the engine's API namespace declares its own
    /// <c>Func</c>.</param>
    /// <param name="getBlock">Looks an asset location up in the block registry.</param>
    /// <param name="itemOrBlockCode">The item or block code to resolve.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>The resolved stack.</returns>
    /// <exception cref="ArgumentException">Thrown when the code resolves to neither a known item
    /// nor a known block.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the quantity is below one, or
    /// above the resolved collectible's max stack size.</exception>
    internal static ItemStack ResolveStack(
        System.Func<AssetLocation, Item?> getItem,
        System.Func<AssetLocation, Block?> getBlock,
        string itemOrBlockCode,
        int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        var location = new AssetLocation(itemOrBlockCode);
        Item? item = getItem(location);
        Block? block = item == null ? getBlock(location) : null;
        ItemStack stack = item != null ? new ItemStack(item, quantity)
            : block is { IsMissing: false } ? new ItemStack(block, quantity)
            : throw new ArgumentException($"Unknown item or block code '{location}'", nameof(itemOrBlockCode));

        // Cap check happens after resolving the stack, since MaxStackSize is a property of the
        // resolved collectible, not of the raw code/quantity pair.
        int maxStackSize = stack.Collectible.MaxStackSize;
        if (quantity > maxStackSize)
        {
            string message = $"'{itemOrBlockCode}' has a max stack size of {maxStackSize}; {quantity} " +
                "exceeds it. Give it in multiple calls/slots instead of one oversized stack.";
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, message);
        }

        return stack;
    }
}
