using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Pins <c>JoinPlayer(name, JoinOptions)</c>: the role goes through the <c>PlayerJoin</c>
/// recipe <see cref="JoinPlayerRoleTests"/> pins (so it carries that recipe's limits), and
/// <c>CollectItems = false</c> turns the player's item pickup off through the engine's own
/// per-player collect mode, shown with an item dropped at the player's feet next to a default
/// player that picks its own up in the same window.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 940404)]
public class JoinOptionsTests : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task JoinPlayer_Should_PutThePlayerOnTheRole_When_TheOptionsNameOne()
    {
        string? seenByEarlyJoinHandler = null;
        string? seenWhenPlaying = null;

        void EarlyJoin(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "OptRole")
            {
                seenByEarlyJoinHandler = joiner.Role.Code;
            }
        }

        void NowPlaying(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "OptRole")
            {
                seenWhenPlaying = joiner.Role.Code;
            }
        }

        // Both are subscribed before the call, so before Atlas's own PlayerJoin handler: the
        // first one is the mod-under-test's position in the order, the second runs later in the
        // join (when the client reaches Playing), after the role was lowered.
        World.Api.Event.PlayerJoin += EarlyJoin;
        World.Api.Event.PlayerNowPlaying += NowPlaying;
        ITestPlayer player;
        try
        {
            player = await World.JoinPlayer("OptRole", new JoinOptions { Role = "suplayer" });
        }
        finally
        {
            World.Api.Event.PlayerNowPlaying -= NowPlaying;
            World.Api.Event.PlayerJoin -= EarlyJoin;
        }

        Assert.Equal("suplayer", player.Player.Role.Code);
        Assert.False(player.Player.HasPrivilege(Privilege.controlserver));
        Assert.Equal("admin", seenByEarlyJoinHandler);
        Assert.Equal("suplayer", seenWhenPlaying);
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_KeepTheEnginesRole_When_TheOptionsNameNone()
    {
        ITestPlayer player = await World.JoinPlayer("OptNoRole", new JoinOptions());

        Assert.Equal("admin", player.Player.Role.Code);
        Assert.True(player.Player.HasPrivilege(Privilege.controlserver));
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_LeaveNoHandlerBehind_When_TheJoinSucceedsOrFails()
    {
        int before = EngineProbes.PlayerJoinHandlers(World.Api);

        await World.JoinPlayer("OptHandlerOk", new JoinOptions { Role = "suplayer" });
        Assert.Equal(before, EngineProbes.PlayerJoinHandlers(World.Api));

        // The server turns a name over the engine's limit away before the entity exists.
        await Assert.ThrowsAsync<AtlasSetupException>(
            () => World.JoinPlayer("ThisPlayerNameIsFarTooLongToBeAccepted", new JoinOptions { Role = "suplayer" }));
        Assert.Equal(before, EngineProbes.PlayerJoinHandlers(World.Api));
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_ReturnThePlayerOnTheHighestRole_When_AHandlerReadsTheRecordAfterTheRoleWasApplied()
    {
        // The limit the docs state: the role holds only until the engine next reads the player's
        // record, and a mod granting a privilege from a later join event does exactly that.
        void GrantWhenPlaying(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "OptRecordRead")
            {
                World.Api.Permissions.GrantPrivilege(joiner.PlayerUID, "atlas-unrelated-privilege");
            }
        }

        World.Api.Event.PlayerNowPlaying += GrantWhenPlaying;
        ITestPlayer player;
        try
        {
            player = await World.JoinPlayer("OptRecordRead", new JoinOptions { Role = "suplayer" });
        }
        finally
        {
            World.Api.Event.PlayerNowPlaying -= GrantWhenPlaying;
        }

        Assert.Equal("admin", player.Player.Role.Code);
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_Throw_When_TheRoleIsNotConfigured_And_LeaveTheNameFree()
    {
        int before = EngineProbes.PlayerJoinHandlers(World.Api);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => World.JoinPlayer("OptBadRole", new JoinOptions { Role = "no-such-role" }));

        Assert.Equal("options", ex.ParamName);
        Assert.Contains("'no-such-role'", ex.Message);
        Assert.Contains("suplayer", ex.Message);
        Assert.Contains("admin", ex.Message);
        Assert.Equal(before, EngineProbes.PlayerJoinHandlers(World.Api));

        ITestPlayer retry = await World.JoinPlayer("OptBadRole");
        Assert.Equal("admin", retry.Player.Role.Code);
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_Throw_When_TheOptionsAreMissing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => World.JoinPlayer("OptNull", null!));

        ITestPlayer retry = await World.JoinPlayer("OptNull");
        Assert.NotNull(retry.Entity);
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_LeaveItemsOnTheGround_When_CollectItemsIsOff_And_AControlPicksItsOwnUp()
    {
        ITestPlayer idle = await World.JoinPlayer("OptNoCollect", new JoinOptions { CollectItems = false });
        ITestPlayer control = await World.JoinPlayer("OptCollects");

        // Out of the spawn scatter, where the other scenarios' players land, and far enough
        // apart that neither player is within reach of the other's item.
        Entity idleItem = await StandAndDrop(idle, 96, 96);
        Entity controlItem = await StandAndDrop(control, 136, 96);

        // The control proves the window is long enough for a pickup, the engine's own delay
        // before a dropped item can be collected included; the idle player then gets as long
        // again, with its item lying in its reach the whole time.
        await WaitUntilCollected(controlItem, control);
        int controlTick = World.CurrentTick;
        await World.Ticks(150);

        Assert.True(idleItem.Alive, $"The item was collected, although the control's was gone {World.CurrentTick - controlTick} ticks ago. {Where(idleItem, idle)}");
        Assert.Equal(0, CountOf(idle, "flint"));
        Assert.Equal(1, CountOf(control, "flint"));
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_LeaveAnItemOnTheGround_When_ItIsDroppedOnTheFirstPassTheEntityExists()
    {
        // The earliest an item can be dropped at the player's feet: a game tick listener
        // subscribed before the join sees the entity as soon as the engine creates it, ahead of
        // the join's own wait for it. The control, joined the same way, picks its item up.
        Item flint = World.Api.World.GetItem(new AssetLocation("game:flint"))!;
        Entity? idleItem = null;
        Entity? controlItem = null;

        void DropWhenTheEntityExists(float dt)
        {
            if (idleItem == null && EngineProbes.PositionOfConnecting(World.Api, "OptFirstPassIdle") is { } idleFeet)
            {
                idleItem = World.Api.World.SpawnItemEntity(new ItemStack(flint), idleFeet)!;
            }

            if (controlItem == null && EngineProbes.PositionOfConnecting(World.Api, "OptFirstPassCtl") is { } controlFeet)
            {
                controlItem = World.Api.World.SpawnItemEntity(new ItemStack(flint), controlFeet)!;
            }
        }

        long listener = World.Api.Event.RegisterGameTickListener(DropWhenTheEntityExists, 1);
        try
        {
            await World.JoinPlayer("OptFirstPassIdle", new JoinOptions { CollectItems = false });
            await World.JoinPlayer("OptFirstPassCtl");
        }
        finally
        {
            World.Api.Event.UnregisterGameTickListener(listener);
        }

        await World.Ticks(60);

        Assert.NotNull(idleItem);
        Assert.NotNull(controlItem);
        Assert.False(controlItem.Alive);
        Assert.True(idleItem.Alive);
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_CollectAgain_When_ThePlayerSneaksAfterCollectItemsWasOff()
    {
        // The limit of the mechanism: the engine's collect mode behind CollectItems = false is
        // "only while sneaking", and a test player never sneaks unless the scenario sets it.
        ITestPlayer player = await World.JoinPlayer("OptSneaker", new JoinOptions { CollectItems = false });
        Entity item = await StandAndDrop(player, -96, -96);
        await World.Ticks(100);
        Assert.True(item.Alive);

        player.Entity.Controls.Sneak = true;
        try
        {
            await WaitUntilCollected(item, player);
        }
        finally
        {
            player.Entity.Controls.Sneak = false;
        }

        Assert.Equal(1, CountOf(player, "flint"));
    }

    [AtlasScenario]
    public async Task JoinPlayer_Should_ApplyBothOptions_When_TheRoleAndCollectItemsAreSet()
    {
        ITestPlayer player = await World.JoinPlayer(
            "OptBoth",
            new JoinOptions { Role = "suplayer", CollectItems = false });
        Entity item = await StandAndDrop(player, -96, 96);

        await World.Ticks(150);

        Assert.Equal("suplayer", player.Player.Role.Code);
        Assert.True(item.Alive);
    }

    [AtlasScenario]
    public async Task WithRole_Should_ShowARealRefusal_And_PutTheRoleBack_When_TheScopeEnds()
    {
        World.Api.ChatCommands.Create("atlasjoinopt")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(_ => TextCommandResult.Success("ran"));
        ITestPlayer player = await World.JoinPlayer("OptWithRole");

        CommandResult asAdmin = await player.ExecuteCommand("/atlasjoinopt");
        Assert.True(asAdmin.Ok, asAdmin.Message);

        using (player.WithRole("suplayer"))
        {
            Assert.Equal("suplayer", player.Player.Role.Code);
            CommandResult refused = await player.ExecuteCommand("/atlasjoinopt");
            Assert.False(refused.Ok);
            Assert.Equal("noprivilege", refused.ErrorCode);
        }

        Assert.Equal("admin", player.Player.Role.Code);
        CommandResult restored = await player.ExecuteCommand("/atlasjoinopt");
        Assert.True(restored.Ok, restored.Message);
    }

    [AtlasScenario]
    public async Task WithRole_Should_RestoreTheArrivalRole_When_TheScopeOpensOnAPlayerJoinedWithOne()
    {
        ITestPlayer player = await World.JoinPlayer("OptWithArrival", new JoinOptions { Role = "suplayer" });

        using (player.WithRole("admin"))
        {
            Assert.Equal("admin", player.Player.Role.Code);
        }

        Assert.Equal("suplayer", player.Player.Role.Code);
    }

    [AtlasScenario]
    public async Task WithRole_Should_Throw_When_TheRoleIsNotConfigured_And_ChangeNothing()
    {
        ITestPlayer player = await World.JoinPlayer("OptWithBad");

        Assert.Throws<ArgumentException>(() => player.WithRole("no-such-role"));

        Assert.Equal("admin", player.Player.Role.Code);
    }

    private async Task WaitUntilCollected(Entity item, ITestPlayer by)
    {
        try
        {
            await World.Until(() => !item.Alive, timeoutTicks: 300);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new Xunit.Sdk.XunitException($"{ex.Message}. {Where(item, by)}");
        }
    }

    private string Where(Entity item, ITestPlayer player)
        => $"Item at {World.PositionOf(item).XYZ}, alive {item.Alive}; player '{player.Player.PlayerName}' at {World.PositionOf(player.Entity).XYZ}, " +
           $"collect mode {player.Player.ItemCollectMode}, sneaking {player.Entity.Controls.Sneak}.";

    /// <summary>Teleports the player to a spot relative to the spawn and drops an item at its
    /// feet. The height is the one the engine placed the player at when it joined, which is the
    /// ground: a test player has no physics of its own, so it stays wherever it is put, and an
    /// item dropped under a player hovering a few blocks up would land out of its reach.</summary>
    private async Task<Entity> StandAndDrop(ITestPlayer player, int dx, int dz)
    {
        await player.TeleportTo(new BlockPos(World.Spawn.X + dx, player.Position.Y, World.Spawn.Z + dz, 0));

        Item flint = World.Api.World.GetItem(new AssetLocation("game:flint"))!;
        return World.Api.World.SpawnItemEntity(new ItemStack(flint), World.PositionOf(player.Entity).XYZ.Add(0, 0.1, 0))!;
    }

    private static int CountOf(ITestPlayer player, string itemCodePart)
    {
        // The hotbar and the backpack, where a pickup lands: the creative inventory the player
        // also owns has no slots to walk.
        IPlayerInventoryManager manager = player.Player.InventoryManager;
        int count = 0;
        foreach (IInventory inventory in new[] { manager.GetHotbarInventory(), manager.GetOwnInventory(GlobalConstants.backpackInvClassName) })
        {
            foreach (ItemSlot slot in inventory)
            {
                if (slot.Itemstack?.Collectible.Code.Path.Contains(itemCodePart, StringComparison.Ordinal) == true)
                {
                    count += slot.Itemstack.StackSize;
                }
            }
        }

        return count;
    }
}
