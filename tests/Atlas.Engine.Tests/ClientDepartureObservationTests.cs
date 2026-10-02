using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers the entity departures of <see cref="IClientObservations"/> (packet 36) and
/// <see cref="IClientObservations.KnowsEntity"/> end to end: what the embedded server really sends
/// a joined test player when an entity despawns, leaves its range, hides from it in the middle of a
/// session or goes away with a rollback restore, on the versions the suite runs.</summary>
/// <remarks><para>The engine reports one despawn through two senders, the despawn queue and the
/// tracking pass, so the scenarios assert on the entity and on what the client knows, never on
/// the number of records or on <see cref="ReceivedEntityDeparture.Reason"/> where two senders can
/// disagree (measured on 1.21.7 and 1.22.3: a despawn reads <c>Death</c> or <c>OutOfRange</c>
/// whatever reason it was asked for, and often both, one to seven passes apart).</para>
/// <para>An observer that moves far from an entity (about 150 blocks and more) is told to unload
/// its chunks and gets no despawn at all, so every scenario that needs a departure moves the
/// entity, not the observer.</para></remarks>
[Trait("Category", "E2E")]
public class ClientDepartureObservationTests
{
    private const string Chicken = "game:chicken-rooster";
    private const int Bound = 400;
    private const int FarBlocks = 700;

    [Fact]
    public async Task Departure_Should_ReachExactlyTheObserversThatKnewTheEntity_When_ItDespawns()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer near = await world.JoinPlayer("NearOne");
            ITestPlayer sameSpot = await world.JoinPlayer("NearTwo");
            ITestPlayer far = await world.JoinPlayer("FarOne");

            // The far observer lives a long way from the first chicken, next to a second one: its
            // departure is the positive control for the absence of the first, and the other way
            // round for the near observers.
            BlockPos away = world.Spawn.Offset(FarBlocks, 0, 0);
            await far.TeleportTo(away);
            Entity here = world.SpawnEntity(Chicken, world.Spawn.Offset(3, 1, 3));
            Entity there = world.SpawnEntity(Chicken, away.Offset(3, 1, 3));
            await world.Until(
                () => near.Client.KnowsEntity(here.EntityId) && sameSpot.Client.KnowsEntity(here.EntityId) && far.Client.KnowsEntity(there.EntityId),
                Bound);
            Assert.False(far.Client.KnowsEntity(here.EntityId));
            Assert.False(near.Client.KnowsEntity(there.EntityId));
            int despawnedAt = world.CurrentTick;

            world.Api.World.DespawnEntity(here, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
            world.Api.World.DespawnEntity(there, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
            await world.Until(
                () => !near.Client.KnowsEntity(here.EntityId) && !sameSpot.Client.KnowsEntity(here.EntityId) && !far.Client.KnowsEntity(there.EntityId),
                Bound);

            // The despawn packets of one pass can be followed by a second sender's, a few passes
            // later: wait them out before asserting what did not happen.
            await world.Ticks(30);
            foreach (ITestPlayer observer in new[] { near, sameSpot })
            {
                ReceivedEntityDeparture[] departures = [.. observer.Client.EntityDepartures().Where(d => d.EntityId == here.EntityId)];
                Assert.True(departures.Length > 0, "no departure for the despawned entity");
                Assert.All(departures, d => Assert.InRange(d.Tick, despawnedAt, world.CurrentTick));
                Assert.All(departures, d => Assert.NotNull(d.Reason));
                Assert.DoesNotContain(observer.Client.EntityDepartures(), d => d.EntityId == there.EntityId);

                // After the arrival, in the order the player received them, and the arrival is still
                // on record: HasReceivedEntity does not mean "currently there".
                ReceivedEntity arrival = observer.Client.EntityArrivals().First(a => a.EntityId == here.EntityId);
                Assert.All(departures, d => Assert.True(d.Sequence > arrival.Sequence));
                Assert.True(observer.Client.HasReceivedEntity(here.EntityId));
                Assert.False(observer.Client.KnowsEntity(here.EntityId));
            }

            Assert.Contains(far.Client.EntityDepartures(), d => d.EntityId == there.EntityId);
            Assert.DoesNotContain(far.Client.EntityDepartures(), d => d.EntityId == here.EntityId);
        });
    }

    [Fact]
    public async Task DespawnPass_Should_SendTheIdsToTheClientThatTrackedTheEntity_And_AnEmptyPacketToTheOthers()
    {
        // Packet 36 has two senders. The despawn queue's goes to EVERY client once per flush and
        // lists the queued entities that client tracks, so it is empty (6 bytes) for a client that
        // tracked none; the tracking pass's only ever carries ids. Measured on 1.21.7 and 1.22.3
        // with one entity: the tracking client parks 12 bytes, usually twice, the other client one
        // 6 byte packet with no ids.
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer tracker = await world.JoinPlayer("DespawnTracker");
            ITestPlayer other = await world.JoinPlayer("DespawnOther");
            await other.TeleportTo(world.Spawn.Offset(FarBlocks, 0, 0));
            Entity chicken = world.SpawnEntity(Chicken, world.Spawn.Offset(3, 1, 3));
            await world.Until(() => tracker.Client.KnowsEntity(chicken.EntityId), Bound);
            await world.Ticks(10);
            tracker.Client.Clear();
            other.Client.Clear();

            world.Api.World.DespawnEntity(chicken, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
            await world.Ticks(30);

            // Any other despawn in the world (wildlife, a drop) flushes a packet to every client
            // too, so the assertions are on the chicken's id: the client that tracked it is sent
            // it, and the client that tracked nothing is sent an empty packet and never the id.
            Packet_EntityDespawn[] trackerSaw = DespawnPackets(tracker);
            Assert.Contains(trackerSaw, packet => Ids(packet).Contains(chicken.EntityId));
            Packet_EntityDespawn[] otherSaw = DespawnPackets(other);
            Assert.Contains(otherSaw, packet => packet.EntityIdCount == 0);
            Assert.All(otherSaw, packet => Assert.DoesNotContain(chicken.EntityId, Ids(packet)));
        });
    }

    [Fact]
    public async Task Departure_Should_BeReportedAsOutOfRange_And_TheEntityKnownAgain_When_APlayerGoesFarAwayAndComesBack()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer observer = await world.JoinPlayer("RangeObserver");
            ITestPlayer walker = await world.JoinPlayer("RangeWalker");
            long walkerId = walker.Entity.EntityId;
            await world.Until(() => observer.Client.KnowsEntity(walkerId), Bound);

            // A window opened while the observer knows him: the entity left the stores, the
            // client did not forget him.
            observer.Client.Clear();
            Assert.False(observer.Client.HasReceivedEntity(walkerId));
            Assert.True(observer.Client.KnowsEntity(walkerId));

            await walker.TeleportTo(world.Spawn.Offset(FarBlocks, 0, 0));
            await world.Until(() => !observer.Client.KnowsEntity(walkerId), Bound);
            ReceivedEntityDeparture left = observer.Client.EntityDepartures().First(d => d.EntityId == walkerId);
            Assert.Equal(EnumDespawnReason.OutOfRange, left.Reason);
            Assert.False(observer.Client.HasReceivedEntity(walkerId));

            await walker.TeleportTo(world.Spawn.Offset(2, 0, 2));
            await world.Until(() => observer.Client.KnowsEntity(walkerId), Bound);
            ReceivedEntity back = observer.Client.EntityArrivals().First(a => a.EntityId == walkerId);
            Assert.True(back.Sequence > left.Sequence);
            Assert.True(back.Tick >= left.Tick);
            Assert.True(observer.Client.HasReceivedEntity(walkerId));
        });
    }

    [Fact]
    public async Task Departure_Should_ShowAnEntityHiddenFromOneObserverMidSession_And_NotFromTheOther()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer observer = await world.JoinPlayer("HideObserver");
            ITestPlayer witness = await world.JoinPlayer("HideWitness");
            ITestPlayer hidden = await world.JoinPlayer("HideSubject");
            long id = hidden.Entity.EntityId;
            await world.Until(() => observer.Client.KnowsEntity(id) && witness.Client.KnowsEntity(id), Bound);

            // The shape of the scenario that stayed green with the filter off: the observer already
            // had the entity, so nothing new is sent to it unless the server takes it away.
            observer.Client.Clear();
            witness.Client.Clear();
            int hiddenAt = world.CurrentTick;
            EngineProbes.HideEntityFrom(world.Api, observer, hidden.Entity);

            // A send reaches the receive buffer at once, so the read in the same pass sees it.
            ReceivedEntityDeparture departure = Assert.Single(observer.Client.EntityDepartures(), d => d.EntityId == id);
            Assert.Equal((id, EnumDespawnReason.Unload, hiddenAt), (departure.EntityId, departure.Reason, departure.Tick));
            Assert.False(observer.Client.KnowsEntity(id));
            Assert.True(witness.Client.KnowsEntity(id));
            Assert.DoesNotContain(witness.Client.EntityDepartures(), d => d.EntityId == id);

            // Lifted again (the vanilla tracking pass has no filter): the client knows him again and
            // the arrival is stamped after the departure.
            await world.Until(() => observer.Client.KnowsEntity(id), Bound);
            ReceivedEntity shown = observer.Client.EntityArrivals().First(a => a.EntityId == id);
            Assert.True(shown.Sequence > departure.Sequence);
            Assert.True(shown.Tick > departure.Tick);
        });
    }

    [Fact]
    public async Task Departure_Should_ReachAnObserver_When_AKnownPlayerDisconnects()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer observer = await world.JoinPlayer("LeaveObserver");
            ITestPlayer leaver = await world.JoinPlayer("LeaveSubject");
            long id = leaver.Entity.EntityId;
            string uid = leaver.Player.PlayerUID;
            await world.Until(() => observer.Client.KnowsEntity(id), Bound);

            leaver.Player.Disconnect("leaving");
            await world.Until(() => !observer.Client.KnowsEntity(id), Bound);

            // The player-data departure says the player left; the entity's own departure says the
            // client dropped the entity. Both arrive.
            Assert.Contains(observer.Client.EntityDepartures(), d => d.EntityId == id);
            await world.Until(() => observer.Client.PlayerData().Any(d => d.IsDeparture && d.PlayerUid == uid), Bound);
        });
    }

    [Fact]
    public async Task Departure_Should_NotFollowADimensionChange_Because_TheEngineTracksByCoordinates()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer observer = await world.JoinPlayer("DimObserver");
            ITestPlayer traveller = await world.JoinPlayer("DimTraveller");
            long id = traveller.Entity.EntityId;
            await world.Until(() => observer.Client.KnowsEntity(id), Bound);
            observer.Client.Clear();

            await traveller.TeleportTo(new BlockPos(world.Spawn.X + 2, world.Spawn.Y, world.Spawn.Z + 2, 1));
            Assert.Equal(1, traveller.Position.dimension);

            // Longer than the engine takes to report a departure (seven passes at most, measured).
            await world.Ticks(60);
            Assert.True(observer.Client.KnowsEntity(id));
            Assert.DoesNotContain(observer.Client.EntityDepartures(), d => d.EntityId == id);

            // The control: the same observer does get a departure when the traveller goes far, in
            // the same window, so the silence above is not a deaf observer.
            await traveller.TeleportTo(world.Spawn.Offset(FarBlocks, 0, 0));
            await world.Until(() => !observer.Client.KnowsEntity(id), Bound);
            Assert.Equal(EnumDespawnReason.OutOfRange, observer.Client.EntityDepartures().First(d => d.EntityId == id).Reason);
        });
    }

    [Fact]
    public async Task Departure_Should_HaveNoArrivalToPairWith_When_ARollbackRestoreRemovesTheEntity()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();

        ITestPlayer player = null!;
        await host.RunScenarioAsync(async world => player = await world.JoinPlayer("RollbackWatcher"));
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        Entity chicken = null!;
        int lastSequenceBefore = 0;
        await host.RunScenarioAsync(async world =>
        {
            chicken = world.SpawnEntity(Chicken, world.Spawn.Offset(4, 1, 4));
            await world.Until(() => player.Client.KnowsEntity(chicken.EntityId), Bound);

            // Chat lines nobody reads: parked at the restore, which is where they must go.
            for (int line = 0; line < 12; line++)
            {
                player.Player.SendMessage(GlobalConstants.GeneralChatGroup, $"unread before the restore {line}", EnumChatType.Notification);
            }

            await world.Ticks(2);
            Assert.True(player.Client.UnreadPackets >= 12, $"{player.Client.UnreadPackets} unread packets");
            lastSequenceBefore = player.Client.EntityArrivals().Max(a => a.Sequence);
        });
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(async world =>
        {
            // The entity was spawned after the capture, so the restore removes it, and the server
            // says so a few passes after the restore cleared the stores: a departure the arrival
            // of which the clear has taken.
            await world.Until(() => !player.Client.KnowsEntity(chicken.EntityId), Bound);
            ReceivedEntityDeparture[] departures = [.. player.Client.EntityDepartures().Where(d => d.EntityId == chicken.EntityId)];
            Assert.True(departures.Length > 0, "no departure for the entity the restore removed");
            Assert.All(departures, d => Assert.True(d.Sequence > lastSequenceBefore));
            Assert.DoesNotContain(player.Client.EntityArrivals(), a => a.EntityId == chicken.EntityId);
            Assert.False(player.Client.HasReceivedEntity(chicken.EntityId));
            Assert.Null(world.Api.World.GetEntityById(chicken.EntityId));

            // What was unread at the restore went with it.
            Assert.DoesNotContain(player.Client.ChatLines(), line => line.StartsWith("unread before the restore", StringComparison.Ordinal));
        });
    }

    private static IEnumerable<long> Ids(Packet_EntityDespawn packet)
        => packet.EntityId?.Take(packet.EntityIdCount) ?? [];

    private static Packet_EntityDespawn[] DespawnPackets(ITestPlayer player)
        => [.. EngineProbes.ParkedPackets(player).Where(packet => packet.Id == 36).Select(packet => packet.EntityDespawn)];
}
