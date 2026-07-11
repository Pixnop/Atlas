using Atlas.Internal.Rollback;
using Atlas.XUnit.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers the player-aware half of the world snapshot/rollback (spec
/// docs/specs/2026-07-06-world-snapshot-rollback.md, stage 2, issue #47) at the
/// <see cref="ServerHost"/> seam: a joined test player's state (inventory, position, watched
/// attributes, per-player moddata) is reset to the captured baseline by a rollback; players that
/// joined after the capture are removed, their names freed for a rejoin; and rollbacks on hosts
/// with joined players count as plain successes in the isolation ledger. The authoring surface
/// on top of this is covered by <c>AdapterPlayerRollbackTests</c>.</summary>
[Trait("Category", "E2E")]
public class PlayerRollbackTests
{
    private const string ModDataKey = "atlas-player-rollback";
    private const string ProbeAttribute = "atlasProbe";

    [Fact]
    public async Task TryRollbackWorld_Should_ResetJoinedPlayerToItsCapturedBaseline_When_AScenarioPollutesIt()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();

        ITestPlayer player = null!;
        BlockPos baselinePos = null!;
        int hotbarSlot = 0;

        // Baseline: one joined player with one flint in the active hotbar slot, a per-player
        // moddata entry and a custom watched attribute.
        await host.RunScenarioAsync(async world =>
        {
            player = await world.JoinPlayer("Baseline");
            await player.GiveItem("game:flint", 1);
            player.Player.SetModdata(ModDataKey, new byte[] { 1 });
            player.Entity.WatchedAttributes.SetFloat(ProbeAttribute, 1f);
            await world.Ticks(2);
            baselinePos = player.Position;
            hotbarSlot = player.Player.InventoryManager.ActiveHotbarSlotNumber;
        });

        // The first rollback request captures, WITH the player joined (a setup error before
        // stage 2), and the baseline must survive the capture untouched.
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture with a joined player failed");
        Assert.True(host.HasWorldSnapshot, "the first rollback request did not capture a snapshot");

        // Pollute everything the reset must undo: the flint stack grows (the swapped-inventories
        // duplicate-items case), an extra stack lands in a second slot, the player teleports
        // away, the watched attribute changes and gains a post-capture sibling, and the
        // per-player moddata is overwritten.
        await host.RunScenarioAsync(async world =>
        {
            await player.GiveItem("game:flint", 8);
            IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
            Item? flint = world.Api.World.GetItem(new AssetLocation("game:flint"));
            Assert.NotNull(flint);
            hotbar[hotbarSlot + 1].Itemstack = new ItemStack(flint, 7);
            hotbar[hotbarSlot + 1].MarkDirty();
            player.Player.SetModdata(ModDataKey, new byte[] { 9, 9 });
            player.Entity.WatchedAttributes.SetFloat(ProbeAttribute, 999f);
            player.Entity.WatchedAttributes.SetBool("atlasPollution", true);
            await player.TeleportTo(baselinePos.Offset(40, 3, 40));
            await world.Ticks(2);
            Assert.NotEqual(baselinePos, player.Position);
        });

        RollbackAttempt restoreAttempt = await host.TryRollbackWorldAsync();
        Assert.True(restoreAttempt.Succeeded, "rollback with a joined player failed: " + restoreAttempt.DegradeDetail);

        await host.RunScenarioAsync(async world =>
        {
            await world.Ticks(2);
            Assert.True(player.IsConnected, "the captured player must survive the rollback");
            Assert.Equal(baselinePos, player.Position);

            IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
            ItemSlot slot = hotbar[hotbarSlot];
            Assert.Equal("game:flint", slot.Itemstack?.Collectible?.Code.ToString());
            Assert.Equal(1, slot.Itemstack!.StackSize);
            Assert.True(hotbar[hotbarSlot + 1].Empty, "the post-capture extra stack survived the rollback");

            Assert.Equal(new byte[] { 1 }, player.Player.GetModdata(ModDataKey));
            Assert.Equal(1f, player.Entity.WatchedAttributes.GetFloat(ProbeAttribute));
            Assert.False(
                player.Entity.WatchedAttributes.HasAttribute("atlasPollution"),
                "a watched attribute added after the capture survived the rollback");
        });
    }

    [Fact]
    public async Task TryRollbackWorld_Should_RemovePostCapturePlayers_And_LetTheNameRejoin_When_TheyJoinedAfterCapture()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();

        ITestPlayer keeper = null!;
        await host.RunScenarioAsync(async world =>
        {
            keeper = await world.JoinPlayer("Keeper");
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        ITestPlayer latecomer = null!;
        long latecomerEntityId = 0;
        await host.RunScenarioAsync(async world =>
        {
            latecomer = await world.JoinPlayer("Latecomer");
            latecomerEntityId = latecomer.Entity.EntityId;
            await latecomer.GiveItem("game:flint", 3);
            latecomer.Player.SetModdata(ModDataKey, new byte[] { 7 });
            await world.Ticks(2);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback failed");

        await host.RunScenarioAsync(async world =>
        {
            await world.Ticks(2);

            // The world is back to its captured population: the captured player survived, the
            // post-capture one is gone, entity included, and its identity left no offline trace.
            Assert.True(keeper.IsConnected, "the captured player must survive the rollback");
            Assert.False(latecomer.IsConnected, "the post-capture player must be removed by the rollback");
            Assert.Single(world.Api.World.AllOnlinePlayers);
            Assert.Null(world.Api.World.GetEntityById(latecomerEntityId));
            Assert.DoesNotContain(world.Api.World.AllPlayers, p => p.PlayerName == "Latecomer");

            // The freed name can rejoin, and comes back as a brand-new player: no inherited
            // items, no inherited per-player moddata.
            ITestPlayer rejoined = await world.JoinPlayer("Latecomer");
            Assert.True(rejoined.IsConnected, "the rejoin under the freed name failed");
            Assert.Null(rejoined.Player.GetModdata(ModDataKey));
            ItemSlot? active = rejoined.Player.InventoryManager.ActiveHotbarSlot;
            Assert.True(active?.Empty != false, "the rejoined player inherited the removed player's items");
        });
    }

    [Fact]
    public async Task Capture_Should_RecordJoinedPlayers_When_DrivenDirectly()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("Squatter");
        });

        await host.RunOnGameThreadAsync(async (api, ticks) =>
        {
            var snapshot = WorldSnapshot.Create(api, ticks);

            await snapshot.CaptureAsync();

            Assert.True(snapshot.IsCaptured, "capture with a joined player must succeed since stage 2");
            Assert.Equal(1, snapshot.SnapshotPlayerCount);
        });
    }

    [Fact]
    public async Task RollbackOrRecycle_Should_CountPlainSuccesses_When_PlayersAreJoined()
    {
        ServerHost host = await HostRegistry.GetOrCreateAsync(typeof(PlayerSummaryProbeScenarios));
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("SummaryPlayer");
        });

        RollbackOutcome capture = await HostRegistry.RollbackOrRecycleAsync(typeof(PlayerSummaryProbeScenarios));
        RollbackOutcome restore = await HostRegistry.RollbackOrRecycleAsync(typeof(PlayerSummaryProbeScenarios));

        // Neither request degraded (before stage 2, the first one failed the scenario outright),
        // the host was never recycled, and the isolation summary counts two plain successes.
        Assert.False(capture.Degraded, capture.DegradeDetail);
        Assert.False(restore.Degraded, restore.DegradeDetail);
        Assert.Same(host, restore.Host);
        string? summary = IsolationLedger.DrainSummary(typeof(PlayerSummaryProbeScenarios));
        Assert.NotNull(summary);
        Assert.Contains("2 rollback(s) succeeded, 0 degraded to a full host recycle", summary);
    }

    /// <summary>Marker class owning the <see cref="HostRegistry"/> host of the summary test.
    /// Never runs as a scenario class; it only keys the registry.</summary>
    private sealed class PlayerSummaryProbeScenarios
    {
    }
}
