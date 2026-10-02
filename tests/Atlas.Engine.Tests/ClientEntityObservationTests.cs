using Atlas.Internal.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers the entity, player-data and player-group observations of
/// <see cref="IClientObservations"/> and the per-pass drain behind them (spec
/// docs/specs/2026-07-17-client-observations.md), end to end: what the embedded server really
/// sends a joined test player, over the three entity paths, and what the drain's stamps,
/// decode errors and listener lifetime do on a live host.</summary>
/// <remarks>Entity paths depend on the engine version and on which client of the server an
/// observer is (a vanilla spawn-queue bug skips two thirds of them for packet 34), so the entity
/// scenarios assert on the union of the paths, never on <see cref="ReceivedEntity.Path"/>.</remarks>
[Trait("Category", "E2E")]
public class ClientEntityObservationTests
{
    private const string Chicken = "game:chicken-rooster";
    private const int Bound = 400;

    [Fact]
    public async Task HasReceivedEntity_Should_BecomeTrueOnEveryObserver_When_AnEntitySpawnsNearThem()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Four observers, so the engine's spawn queue (packet 34 reaches only the first third
            // of them) has to hand the entity to the rest by another path.
            ITestPlayer[] observers =
            [
                await world.JoinPlayer("ObsOne"),
                await world.JoinPlayer("ObsTwo"),
                await world.JoinPlayer("ObsThree"),
                await world.JoinPlayer("ObsFour"),
            ];
            foreach (ITestPlayer observer in observers)
            {
                observer.Client.Clear();
            }

            int spawnedAt = world.CurrentTick;
            Entity chicken = world.SpawnEntity(Chicken, world.Spawn.Offset(3, 1, 3));

            await world.Until(() => observers.All(o => o.Client.HasReceivedEntity(chicken.EntityId)), Bound);

            foreach (ITestPlayer observer in observers)
            {
                ReceivedEntity[] arrivals = [.. observer.Client.EntityArrivals().Where(a => a.EntityId == chicken.EntityId)];
                Assert.NotEmpty(arrivals);
                Assert.All(arrivals, a => Assert.Equal("chicken-rooster", a.EntityType));
                Assert.All(arrivals, a => Assert.True(Enum.IsDefined(a.Path), $"unknown path {a.Path}"));

                // Stamped with a pass of this scenario: not before the spawn (the clear emptied
                // everything older), not after the present.
                Assert.All(arrivals, a => Assert.InRange(a.Tick, spawnedAt, world.CurrentTick));
            }

            foreach (ITestPlayer observer in observers)
            {
                IReadOnlyList<ReceivedEntity> all = observer.Client.EntityArrivals();
                Assert.Equal(all.OrderBy(a => a.Sequence).ToArray(), all.ToArray());
                Assert.Equal(all.Select(a => a.Tick).Order().ToArray(), all.Select(a => a.Tick).ToArray());
            }
        });
    }

    [Fact]
    public async Task Observations_Should_ShowAJoiningPlayerToTheOthers_And_TheDepartureWhenHeLeaves()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer first = await world.JoinPlayer("FirstOne");

            // The observer's own entity and own data come with its join: a positive control that
            // needs no wait, and the packet-40 path decoded for real.
            Assert.True(first.Client.HasReceivedEntity(first.Entity.EntityId));
            Assert.Contains(first.Client.EntityArrivals(), a => a.EntityId == first.Entity.EntityId && a.Path == EntityArrivalPath.JoinList);
            Assert.Contains(first.Client.PlayerData(), d => d.IsSelf && d.PlayerUid == first.Player.PlayerUID);

            ITestPlayer second = await world.JoinPlayer("SecondOne");
            string uid = second.Player.PlayerUID;
            await world.Until(
                () => first.Client.HasReceivedEntity(second.Entity.EntityId) && first.Client.HasReceivedPlayerData(uid),
                Bound);

            ReceivedPlayerData about = first.Client.PlayerData().First(d => d.PlayerUid == uid && !d.IsDeparture);
            Assert.Equal("SecondOne", first.Client.PlayerData().Last(d => d.PlayerUid == uid && !d.IsDeparture).PlayerName);
            Assert.False(about.IsSelf);
            Assert.Equal(second.Entity.EntityId, first.Client.PlayerData().Last(d => d.PlayerUid == uid).EntityId);

            // The joiner is told about the player who was already there.
            await world.Until(() => second.Client.HasReceivedPlayerData(first.Player.PlayerUID), Bound);

            second.Player.Disconnect("leaving");
            await world.Until(() => first.Client.PlayerData().Any(d => d.IsDeparture && d.PlayerUid == uid), Bound);

            ReceivedPlayerData departure = first.Client.PlayerData().Last(d => d.PlayerUid == uid);
            Assert.True(departure.IsDeparture);
            Assert.Equal(-99, departure.ClientId);

            // A departure is not data about the player; the earlier record still counts.
            Assert.True(first.Client.HasReceivedPlayerData(uid));
        });
    }

    [Fact]
    public async Task HasReceivedEntity_Should_StayFalseWhileFar_And_BecomeTrueAfterATeleportIntoRange()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer observer = await world.JoinPlayer("FarObserver");
            ITestPlayer neighbour = await world.JoinPlayer("FarNeighbour");

            // The neighbour goes far away and a chicken spawns next to him, out of the
            // observer's range. The neighbour is the positive control: he is in range, so the
            // chicken must reach him, which proves the machinery was listening the whole time.
            BlockPos far = world.Spawn.Offset(700, 0, 0);
            await neighbour.TeleportTo(far);
            Entity chicken = world.SpawnEntity(Chicken, far.Offset(2, 1, 2));
            await world.Until(() => neighbour.Client.HasReceivedEntity(chicken.EntityId), Bound);

            // A generous window, not a bound: no arrival measured took longer than 38 passes, and the
            // control above proves the observer was listening the whole time.
            await world.Ticks(60);
            Assert.False(
                observer.Client.HasReceivedEntity(chicken.EntityId),
                "an entity 700 blocks away reached an observer that never went near it");

            await observer.TeleportTo(far.Offset(-2, 0, -2));
            await world.Until(() => observer.Client.HasReceivedEntity(chicken.EntityId), Bound);
        });
    }

    [Fact]
    public async Task Clear_Should_EmptyEveryNewStore_And_KeepSequencesGrowing()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("ClearOne");
            await world.Ticks(5);
            Assert.NotEmpty(player.Client.EntityArrivals());
            Assert.NotEmpty(player.Client.PlayerData());
            Assert.NotEmpty(player.Client.GroupListings());
            int lastSequence = player.Client.EntityArrivals().Max(a => a.Sequence);

            player.Client.Clear();

            Assert.Empty(player.Client.EntityArrivals());
            Assert.False(player.Client.HasReceivedEntity(player.Entity.EntityId));
            Assert.Empty(player.Client.PlayerData());
            Assert.False(player.Client.HasReceivedPlayerData(player.Player.PlayerUID));
            Assert.Empty(player.Client.GroupListings());
            Assert.Empty(player.Client.GroupUpdates());

            // What arrives afterwards is captured, and ordered after what was cleared.
            await player.Say("/group create afterclear");
            ReceivedGroupUpdate update = Assert.Single(player.Client.GroupUpdates());
            Assert.True(update.Sequence > lastSequence, $"{update.Sequence} must follow {lastSequence}");
        });
    }

    [Fact]
    public async Task Client_Should_ClearTheEntityStores_When_TheWorldIsRolledBack()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();

        ITestPlayer player = null!;
        Entity before = null!;
        await host.RunScenarioAsync(async world =>
        {
            player = await world.JoinPlayer("RollObserver");
            before = world.SpawnEntity(Chicken, world.Spawn.Offset(4, 1, 4));
            await world.Until(() => player.Client.HasReceivedEntity(before.EntityId), Bound);
        });
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        int lastSequenceBefore = 0;
        await host.RunScenarioAsync(async world =>
        {
            Entity after = world.SpawnEntity(Chicken, world.Spawn.Offset(-4, 1, -4));
            await world.Until(() => player.Client.HasReceivedEntity(after.EntityId), Bound);
            await player.Say("/group create beforerollback");
            Assert.NotEmpty(player.Client.GroupUpdates());
            lastSequenceBefore = player.Client.EntityArrivals().Select(a => a.Sequence)
                .Concat(player.Client.PlayerData().Select(d => d.Sequence))
                .Concat(player.Client.GroupListings().Select(l => l.Sequence))
                .Concat(player.Client.GroupUpdates().Select(u => u.Sequence))
                .Max();
        });
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(world =>
        {
            Assert.True(player.IsConnected, "the rollback dropped a player joined before the capture");

            // Everything read before the restore is gone, including an entity the server did send
            // this player earlier and that is still in the world: the restore does not send it
            // again, so "never received" holds afterwards for the wrong reason, which is what the
            // docs warn about.
            Assert.NotNull(world.Api.World.GetEntityById(before.EntityId));
            Assert.Empty(player.Client.EntityArrivals());
            Assert.False(player.Client.HasReceivedEntity(before.EntityId));
            Assert.Empty(player.Client.GroupListings());
            Assert.Empty(player.Client.GroupUpdates());

            // What the restore itself sends is captured normally: it re-syncs the restored player
            // with its own player data, and nothing else. Sequences kept counting across the clear.
            Assert.All(player.Client.PlayerData(), d => Assert.True(d.IsSelf, $"{d.PlayerUid} was cleared by the restore"));
            Assert.All(player.Client.PlayerData(), d => Assert.True(d.Sequence > lastSequenceBefore));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GroupObservations_Should_ShowTheListingOnJoin_TheUpdateOnCreate_And_TheListingWithoutTheGroupOnLeave()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("GroupOne");

            // Every join sends a listing: an empty one is the positive control for the others.
            ReceivedGroupListing onJoin = player.Client.GroupListings()[0];
            Assert.Empty(onJoin.Groups);
            Assert.Empty(player.Client.GroupUpdates());
            int listingsBefore = player.Client.GroupListings().Count;

            await player.Say("/group create probegroup");
            ReceivedGroupUpdate created = Assert.Single(player.Client.GroupUpdates());
            Assert.Equal("probegroup", created.Group.Name);
            Assert.Equal(player.Player.PlayerUID, created.Group.OwnerUid);
            Assert.Equal(EnumPlayerGroupMemberShip.Owner, created.Group.Membership);

            // Creating a group only adds one on a client: no new listing.
            Assert.Equal(listingsBefore, player.Client.GroupListings().Count);

            await player.Say("/group leave probegroup");
            ReceivedGroupListing afterLeave = player.Client.GroupListings()[^1];
            Assert.DoesNotContain(afterLeave.Groups, g => g.Name == "probegroup");
            Assert.True(afterLeave.Sequence > created.Sequence, "the listing that drops the group must come after its creation");
            Assert.True(afterLeave.Tick >= created.Tick);
        });
    }

    [Fact]
    public async Task Tick_Should_BeTheSendTickOrTheNextOne_When_TheScenarioSendsBetweenPasses()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("StampOne");
            await world.Ticks(2);

            // The command sends its group update synchronously, in the middle of this
            // continuation: between two passes.
            int sentAt = world.CurrentTick;
            await player.ExecuteCommand("/group create stampfirst");
            ReceivedGroupUpdate readAtOnce = Assert.Single(player.Client.GroupUpdates());
            Assert.Equal(sentAt, readAtOnce.Tick);

            // Left unread through a few passes, the next pass found it and stamped it.
            await player.ExecuteCommand("/group create stampsecond");
            await world.Ticks(5);
            ReceivedGroupUpdate found = player.Client.GroupUpdates().Single(u => u.Group.Name == "stampsecond");
            Assert.Equal(sentAt + 1, found.Tick);
            Assert.True(found.Sequence > readAtOnce.Sequence);
        });
    }

    [Fact]
    public async Task Read_Should_ThrowOnceNamingThePacket_And_KeepTheRest_When_APacketCannotBeDecoded()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("BadPacket");
            player.Client.Clear();
            await world.Ticks(2);
            player.Client.Clear();

            world.Api.SendMessage(player.Player, GlobalConstants.GeneralChatGroup, "before the bad packet", EnumChatType.Notification);
            EngineProbes.SendUndecodableParticles(world.Api, player.Player);
            world.Api.SendMessage(player.Player, GlobalConstants.GeneralChatGroup, "after the bad packet", EnumChatType.Notification);
            await world.Ticks(2);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => player.Client.ChatLines());
            Assert.Contains("packet id 61", failure.Message);
            Assert.Contains("Tick ", failure.Message);
            Assert.Contains("Sequence ", failure.Message);
            Assert.NotNull(failure.InnerException);

            // Once: the bad packet is gone, the packets around it were kept.
            Assert.Equal(["before the bad packet", "after the bad packet"], player.Client.ChatLines());
            Assert.Empty(player.Client.Particles());
        });
    }

    [Fact]
    public async Task Particles_Should_DecodeBlockCubeParticles_When_TheServerSpawnsThem()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("CubeObserver");
            player.Client.Clear();
            BlockPos at = player.Position;

            // The engine only sends particles to players it already streamed the chunk to: spawn
            // once per pass until one lands (the first packet of the series is the only one).
            await world.Until(
                () =>
                {
                    world.Api.World.SpawnCubeParticles(at.Offset(0, -1, 0), new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5), 0.5f, 3, 1f);
                    return player.Client.Particles().Count > 0;
                },
                timeoutTicks: 600);

            SpawnedParticles spawn = Assert.Single(player.Client.Particles());
            Assert.IsType<BlockCubeParticles>(spawn.Provider);
            Assert.Equal(3f, spawn.Quantity);
        });
    }

    [Fact]
    public async Task Drain_Should_AllocateLittleAndLeaveNoListenerBehind_When_PlayersJoinKickAndLeaveByEveryPath()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();

        // Guard on the cost: the per-pass listener only dequeues, so on a quiet one-player world
        // a pass costs it next to nothing. Measured by calling it directly, the way the engine
        // does, with the game thread's own allocation counter; timing is not asserted.
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("QuietOne");
            var observations = (ClientObservations)player.Client;
            await world.Ticks(30);
            player.Client.Clear();

            const int Passes = 300;
            long allocated = 0;
            for (int pass = 0; pass < Passes; pass++)
            {
                await world.Ticks(1);
                long before = GC.GetAllocatedBytesForCurrentThread();
                observations.OnPass(0.033f);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Assert.True(allocated / Passes < 1024, $"the per-pass listener allocated {allocated / Passes} bytes per pass");
        });

        // Lifetime: a kick, a client leave, a rollback removal and thirty join and kick cycles
        // leave the engine's tick listener count where it started.
        ITestPlayer keeper = null!;
        await host.RunScenarioAsync(async world => keeper = await world.JoinPlayer("KeeperOne"));
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        await host.RunScenarioAsync(async world =>
        {
            int baseline = TickListeners(world.Api);

            for (int cycle = 0; cycle < 30; cycle++)
            {
                ITestPlayer cycled = await world.JoinPlayer($"Cycle{cycle}");
                Assert.True(TickListeners(world.Api) > baseline, "a joined player registered no listener at all");
                cycled.Player.Disconnect("cycle");
                await world.Until(() => !cycled.IsConnected, Bound);
                await world.Until(() => TickListeners(world.Api) == baseline, Bound);
            }

            // What a kicked player received stays readable after its listener is gone.
            ITestPlayer kicked = await world.JoinPlayer("KickedOne");
            kicked.Player.Disconnect("kicked");
            await world.Until(() => TickListeners(world.Api) == baseline, Bound);
            Assert.NotEmpty(kicked.Client.PlayerData());
            Assert.True(kicked.Client.HasReceivedEntity(kicked.Entity.EntityId));

            // A client closing its connection, the packet a real client sends on exit.
            ITestPlayer leaver = await world.JoinPlayer("LeaverOne");
            EngineProbes.SendClientLeave(leaver);
            await world.Until(() => !leaver.IsConnected, Bound);
            await world.Until(() => TickListeners(world.Api) == baseline, Bound);
            Assert.NotEmpty(leaver.Client.PlayerData());

            // A player removed by a rollback restore: joins after the capture, goes with it.
            Assert.True(keeper.IsConnected);
        });

        ITestPlayer latecomer = null!;
        int withoutLatecomer = 0;
        await host.RunScenarioAsync(async world =>
        {
            withoutLatecomer = TickListeners(world.Api);
            latecomer = await world.JoinPlayer("LatecomerOne");
            Assert.True(TickListeners(world.Api) > withoutLatecomer);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(async world =>
        {
            await world.Until(() => !latecomer.IsConnected, Bound);
            await world.Until(() => TickListeners(world.Api) == withoutLatecomer, Bound);
            Assert.True(keeper.IsConnected);

            // Cleared by the restore that removed it like every other player, then frozen: still
            // readable, nothing throws.
            Assert.NotNull(latecomer.Client.PlayerData());
        });
    }

    private static int TickListeners(ICoreServerAPI api) => EngineProbes.TickListeners(api);
}
