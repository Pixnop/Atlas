using Atlas.Internal.Player;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Common;

namespace Atlas.Pure.Tests.Player;

/// <summary>Tests for the per-pass drain of <see cref="ClientObservations"/>: what the tick
/// listener parks and stamps, what a read decodes and in which order, and what happens to a
/// packet that cannot be decoded. The dummy connection is replaced by a queue of
/// <see cref="NetIncomingMessage"/> holding bytes built with the engine's own serializer, and the
/// server API by a substitute that hands back the registered listeners; the live engine paths are
/// in the E2E suite.</summary>
public class ClientObservationsDrainTests
{
    private const string OwnUid = "uid-own";

    /// <summary>Whether the engine this suite runs against can unregister an event bus listener
    /// (1.22 and later): looked up rather than called, as the code under test does, so the suite
    /// still compiles against the 1.21 floor.</summary>
    private static readonly int ExpectedHookUnregistrations
        = typeof(IEventAPI).GetMethod("UnregisterEventBusListener") != null ? 1 : 0;

    [Fact]
    public void Constructor_Should_RegisterAPerPassListenerWithAnErrorHandler_And_TheRestoredHook()
    {
        var harness = new Harness();

        Assert.NotNull(harness.OnPass);
        Assert.NotNull(harness.OnError);
        Assert.Equal(1, harness.TickIntervalMs);
        Assert.NotNull(harness.OnRestored);
    }

    [Fact]
    public void Listener_Should_StampEachPacketWithThePassItRanIn_And_NumberThemInArrivalOrder()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 5);
        harness.Send(EntityPacket(3));
        harness.RunPass(tick: 6);

        Assert.Equal(
            [(1L, 5, 0), (2L, 5, 1), (3L, 6, 2)],
            harness.Observations.EntityArrivals().Select(e => (e.EntityId, e.Tick, e.Sequence)).ToArray());
    }

    [Fact]
    public void Read_Should_StampWithTheTickOfTheRead_When_NoPassRanBetweenTheSendAndTheRead()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Tick = 9;

        ReceivedEntity arrival = Assert.Single(harness.Observations.EntityArrivals());

        Assert.Equal((9, 0), (arrival.Tick, arrival.Sequence));
    }

    [Fact]
    public void Read_Should_DecodeParkedPacketsBeforeTheOnesStillInTheEngineQueue()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 3);
        harness.Send(EntityPacket(2));
        harness.Tick = 4;

        IReadOnlyList<ReceivedEntity> arrivals = harness.Observations.EntityArrivals();

        Assert.Equal([(1L, 3, 0), (2L, 4, 1)], arrivals.Select(e => (e.EntityId, e.Tick, e.Sequence)).ToArray());
    }

    [Fact]
    public void Tick_Should_NeverDecrease_AsSequenceGrows()
    {
        var harness = new Harness();

        for (int tick = 1; tick <= 20; tick++)
        {
            harness.Send(EntityPacket(tick));
            if (tick % 3 != 0)
            {
                harness.RunPass(tick);
            }
        }

        harness.Tick = 21;
        IReadOnlyList<ReceivedEntity> arrivals = harness.Observations.EntityArrivals();

        Assert.Equal(20, arrivals.Count);
        Assert.Equal(arrivals.OrderBy(a => a.Sequence).ToArray(), arrivals.ToArray());
        Assert.Equal(arrivals.Select(a => a.Tick).OrderBy(t => t).ToArray(), arrivals.Select(a => a.Tick).ToArray());
    }

    [Fact]
    public void Read_Should_ShareOneSequenceAcrossEveryKindOfPacket()
    {
        var harness = new Harness();

        harness.Send(PlayerDataPacket("uid-b", clientId: 2));
        harness.Send(EntityPacket(5));
        harness.Send(GroupListingPacket("g"));
        harness.Send(GroupUpdatePacket("g2"));
        harness.RunPass(tick: 2);

        Assert.Equal(0, harness.Observations.PlayerData().Single().Sequence);
        Assert.Equal(1, harness.Observations.EntityArrivals().Single().Sequence);
        Assert.Equal(2, harness.Observations.GroupListings().Single().Sequence);
        Assert.Equal(3, harness.Observations.GroupUpdates().Single().Sequence);
    }

    [Fact]
    public void Read_Should_GiveTheEntitiesOfOnePacketTheSameStamp()
    {
        var harness = new Harness();

        harness.Send(new Packet_Server
        {
            Id = 34,
            EntitySpawn = new Packet_EntitySpawn
            {
                Entity = [Entity(1), Entity(2), null!],
                EntityCount = 2,
                EntityLength = 2,
            },
        });
        harness.Send(EntityPacket(3));
        harness.RunPass(tick: 4);

        Assert.Equal(
            [(1L, EntityArrivalPath.Spawn, 0), (2L, EntityArrivalPath.Spawn, 0), (3L, EntityArrivalPath.TrackedRange, 1)],
            harness.Observations.EntityArrivals().Select(e => (e.EntityId, e.Path, e.Sequence)).ToArray());
    }

    [Fact]
    public void Read_Should_IgnoreAnEmptyMessage_And_StillNumberIt()
    {
        // A zero-length message deserializes to a packet with no sub-message: seen live, so the
        // drain must take it in stride rather than report it as a failure.
        var harness = new Harness();

        harness.SendBytes([]);
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);

        ReceivedEntity arrival = Assert.Single(harness.Observations.EntityArrivals());
        Assert.Equal(1, arrival.Sequence);
    }

    [Fact]
    public void HasReceivedEntity_Should_AnswerOverTheUnionOfThePaths()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Send(new Packet_Server { Id = 40, Entities = new Packet_Entities { Entities = [Entity(2)], EntitiesCount = 1, EntitiesLength = 1 } });
        harness.RunPass(tick: 1);

        Assert.True(harness.Observations.HasReceivedEntity(1));
        Assert.True(harness.Observations.HasReceivedEntity(2));
        Assert.False(harness.Observations.HasReceivedEntity(3));
    }

    [Fact]
    public void EntityDepartures_Should_ListEveryDespawnTheServerSent_WithTheTickAndSequenceOfItsPacket()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Send(DespawnPacket((1, EnumDespawnReason.Death), (2, EnumDespawnReason.OutOfRange)));
        harness.RunPass(tick: 5);
        harness.Send(DespawnPacket((3, EnumDespawnReason.Disconnect)));
        harness.RunPass(tick: 6);

        Assert.Equal(
            [
                new ReceivedEntityDeparture(1, EnumDespawnReason.Death, 5, 1),
                new ReceivedEntityDeparture(2, EnumDespawnReason.OutOfRange, 5, 1),
                new ReceivedEntityDeparture(3, EnumDespawnReason.Disconnect, 6, 2),
            ],
            harness.Observations.EntityDepartures());

        // A read does not consume them.
        Assert.Equal(3, harness.Observations.EntityDepartures().Count);
    }

    [Fact]
    public void EntityDepartures_Should_StayEmpty_When_TheEngineSendsAnEmptyDespawn()
    {
        var harness = new Harness();

        harness.Send(DespawnPacket());
        harness.RunPass(tick: 1);

        Assert.Empty(harness.Observations.EntityDepartures());
    }

    [Fact]
    public void KnowsEntity_Should_FollowTheLastThingTheServerToldTheClient_ArrivalOrDeparture()
    {
        var harness = new Harness();

        Assert.False(harness.Observations.KnowsEntity(1));

        harness.Send(EntityPacket(1));
        harness.Send(new Packet_Server { Id = 34, EntitySpawn = new Packet_EntitySpawn { Entity = [Entity(2)], EntityCount = 1, EntityLength = 1 } });
        harness.Send(new Packet_Server { Id = 40, Entities = new Packet_Entities { Entities = [Entity(3)], EntitiesCount = 1, EntitiesLength = 1 } });
        harness.RunPass(tick: 1);
        Assert.True(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.KnowsEntity(2));
        Assert.True(harness.Observations.KnowsEntity(3));

        harness.Send(DespawnPacket((1, EnumDespawnReason.Death), (3, EnumDespawnReason.OutOfRange)));
        harness.RunPass(tick: 2);
        Assert.False(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.KnowsEntity(2));
        Assert.False(harness.Observations.KnowsEntity(3));

        // Back into range: the client knows it again, and HasReceivedEntity never stopped saying yes.
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 3);
        Assert.True(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.HasReceivedEntity(3));
        Assert.False(harness.Observations.KnowsEntity(3));
    }

    [Fact]
    public void KnowsEntity_Should_OrderByThePacketsNotByTheTick_When_AnArrivalAndADeparturePrecedeEachOther()
    {
        // Two packets of one pass: the later one in the player's order wins, as on a client.
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Send(DespawnPacket((1, EnumDespawnReason.Death)));
        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 1);
        harness.Send(DespawnPacket((2, EnumDespawnReason.Death)));
        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 1);

        Assert.False(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.KnowsEntity(2));
    }

    [Fact]
    public void KnowsEntity_Should_ReadThePacketsStillInTheEnginesQueue()
    {
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.Tick = 3;

        Assert.True(harness.Observations.KnowsEntity(1));
    }

    [Fact]
    public void Clear_Should_NotMakeTheClientForgetAnEntityItWasToldAbout_ThoughHasReceivedEntityDoes()
    {
        // The trap this guards: a Clear() that opens a window must not turn "the client knows the
        // entity" into "false", or an assertion that it is gone passes for the wrong reason.
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);
        harness.Send(EntityPacket(2));

        harness.Observations.Clear();

        Assert.False(harness.Observations.HasReceivedEntity(1));
        Assert.False(harness.Observations.HasReceivedEntity(2));
        Assert.Empty(harness.Observations.EntityArrivals());
        Assert.True(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.KnowsEntity(2), "an arrival still queued at the clear was lost");
    }

    [Fact]
    public void Clear_Should_ApplyTheDeparturesItDrops_ToWhatTheClientKnows()
    {
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 1);
        harness.Send(DespawnPacket((1, EnumDespawnReason.Death)));
        harness.RunPass(tick: 2);
        harness.Send(DespawnPacket((2, EnumDespawnReason.OutOfRange)));

        harness.Observations.Clear();

        Assert.Empty(harness.Observations.EntityDepartures());
        Assert.False(harness.Observations.KnowsEntity(1), "a parked departure was lost by the clear");
        Assert.False(harness.Observations.KnowsEntity(2), "a queued departure was lost by the clear");
    }

    [Fact]
    public void Clear_Should_NotThrow_When_AnEntityPacketItAppliesCannotBeDecoded()
    {
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);
        harness.SendBytes([0xD0, 0x05, 33, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        harness.Observations.Clear();

        Assert.True(harness.Observations.KnowsEntity(1));
        Assert.Equal(0, harness.Pending);
    }

    [Fact]
    public void RestoredHook_Should_KeepWhatTheClientKnows_And_NotListADepartureItHasNoArrivalFor()
    {
        // After a rollback restore the stores are empty, then the server despawns what the restore
        // removed: a departure with no arrival in the store. It is listed, and the entity stops
        // being known, whether or not the arrival survived the clear.
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 1);

        EnumHandling handling = EnumHandling.PassThrough;
        harness.OnRestored!("atlas:rollback:restored", ref handling, new TreeAttribute());
        Assert.Empty(harness.Observations.EntityArrivals());
        Assert.True(harness.Observations.KnowsEntity(1));

        harness.Send(DespawnPacket((1, EnumDespawnReason.Death)));
        harness.RunPass(tick: 9);

        Assert.Equal([1L], harness.Observations.EntityDepartures().Select(d => d.EntityId).ToArray());
        Assert.Empty(harness.Observations.EntityArrivals());
        Assert.False(harness.Observations.KnowsEntity(1));
        Assert.True(harness.Observations.KnowsEntity(2));
    }

    [Fact]
    public void EntityDepartures_Should_BeEmptiedByClear_And_KeepTheSequenceCounting()
    {
        var harness = new Harness();
        harness.Send(DespawnPacket((1, EnumDespawnReason.Death)));
        harness.RunPass(tick: 1);
        Assert.NotEmpty(harness.Observations.EntityDepartures());

        harness.Observations.Clear();
        Assert.Empty(harness.Observations.EntityDepartures());

        harness.Send(DespawnPacket((2, EnumDespawnReason.Death)));
        harness.RunPass(tick: 2);
        Assert.Equal(1, Assert.Single(harness.Observations.EntityDepartures()).Sequence);
    }

    [Fact]
    public void KnowsEntity_Should_ReportANeverSeenEntityAsUnknown_When_ItsDepartureArrives()
    {
        var harness = new Harness();

        harness.Send(DespawnPacket((5, EnumDespawnReason.OutOfRange)));
        harness.RunPass(tick: 1);

        Assert.False(harness.Observations.KnowsEntity(5));
        Assert.False(harness.Observations.HasReceivedEntity(5));
        Assert.Single(harness.Observations.EntityDepartures());
    }

    [Fact]
    public void PlayerData_Should_FlagTheOwnUid_And_HasReceivedPlayerData_Should_ExcludeDepartures()
    {
        var harness = new Harness();

        harness.Send(PlayerDataPacket(OwnUid, clientId: 1));
        harness.Send(PlayerDataPacket("uid-b", clientId: 2));
        harness.Send(PlayerDataPacket("uid-b", clientId: -99));
        harness.Send(PlayerDataPacket("uid-gone", clientId: -99));
        harness.RunPass(tick: 1);

        IReadOnlyList<ReceivedPlayerData> records = harness.Observations.PlayerData();

        Assert.Equal([true, false, false, false], records.Select(r => r.IsSelf).ToArray());
        Assert.Equal([false, false, true, true], records.Select(r => r.IsDeparture).ToArray());
        Assert.True(harness.Observations.HasReceivedPlayerData(OwnUid));
        Assert.True(harness.Observations.HasReceivedPlayerData("uid-b"));
        Assert.False(harness.Observations.HasReceivedPlayerData("uid-gone"));
        Assert.False(harness.Observations.HasReceivedPlayerData("uid-never"));
    }

    [Fact]
    public void Read_Should_KeepTheFourOlderKindsWorking_When_TheyArriveBetweenTheNewOnes()
    {
        var harness = new Harness();

        harness.Send(new Packet_Server { Id = 8, Chatline = new Packet_ChatLine { Message = "hello", Groupid = 0, ChatType = (int)EnumChatType.Notification } });
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);

        Assert.Equal(["hello"], harness.Observations.ChatLines());
        Assert.True(harness.Observations.HasReceivedEntity(1));
    }

    [Fact]
    public void Read_Should_ThrowOnceNamingThePacket_And_KeepTheOnesBehindIt_When_OneParkedPacketCannotBeDecoded()
    {
        var harness = new Harness();
        harness.Api.ClassRegistry.CreateParticlePropertyProvider(Arg.Any<string>()).Throws(new InvalidOperationException("unknown provider"));

        harness.Send(EntityPacket(1));
        harness.Send(new Packet_Server { Id = 61, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "nope", Data = [] } });
        harness.Send(EntityPacket(3));
        harness.RunPass(tick: 7);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => harness.Observations.EntityArrivals());

        Assert.Contains("61", failure.Message);
        Assert.Contains("Tick 7", failure.Message);
        Assert.Contains("Sequence 1", failure.Message);
        Assert.Equal("unknown provider", failure.InnerException?.Message);

        // Surfaced once: the failed packet is gone, and what was behind it was kept.
        Assert.Equal([1L, 3L], harness.Observations.EntityArrivals().Select(e => e.EntityId).ToArray());
        Assert.Empty(harness.Observations.Particles());
    }

    [Fact]
    public void Read_Should_NameTheNumberOfFailures_When_SeveralPacketsCannotBeDecoded()
    {
        var harness = new Harness();
        harness.Api.ClassRegistry.CreateParticlePropertyProvider(Arg.Any<string>()).Throws(new InvalidOperationException("unknown provider"));

        harness.Send(new Packet_Server { Id = 61, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "a", Data = [] } });
        harness.Send(new Packet_Server { Id = 61, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "b", Data = [] } });
        harness.RunPass(tick: 2);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => harness.Observations.Particles());

        Assert.Contains("Sequence 0", failure.Message);
        Assert.Contains("1 more", failure.Message);
        harness.Observations.Particles();
    }

    [Fact]
    public void Read_Should_ThrowTheListenersStoredError_Once()
    {
        var harness = new Harness();
        var boom = new InvalidOperationException("boom");

        harness.OnError!(boom);
        harness.OnError!(new InvalidOperationException("second, ignored"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => harness.Observations.Chat());

        Assert.Same(boom, failure.InnerException);
        Assert.Empty(harness.Observations.Chat());
    }

    [Fact]
    public void Clear_Should_DropParkedAndQueuedPackets_WithoutDecodingTheKindsThatCanFail_And_ForgetAStoredError()
    {
        var harness = new Harness();
        harness.Api.ClassRegistry.CreateParticlePropertyProvider(Arg.Any<string>()).Throws(new InvalidOperationException("would throw if decoded"));

        harness.Send(new Packet_Server { Id = 61, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "nope", Data = [] } });
        harness.RunPass(tick: 1);
        harness.Send(new Packet_Server { Id = 61, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "nope", Data = [] } });
        harness.OnError!(new InvalidOperationException("stored"));

        harness.Observations.Clear();

        Assert.Empty(harness.Observations.Particles());
        Assert.Equal(0, harness.Pending);
    }

    [Fact]
    public void Clear_Should_EmptyEveryStore_And_KeepTheSequenceCounting()
    {
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.Send(PlayerDataPacket("uid-b", clientId: 2));
        harness.Send(GroupListingPacket("g"));
        harness.Send(GroupUpdatePacket("g2"));
        harness.RunPass(tick: 1);
        Assert.True(harness.Observations.HasReceivedEntity(1));

        harness.Observations.Clear();

        Assert.Empty(harness.Observations.EntityArrivals());
        Assert.False(harness.Observations.HasReceivedEntity(1));
        Assert.Empty(harness.Observations.PlayerData());
        Assert.False(harness.Observations.HasReceivedPlayerData("uid-b"));
        Assert.Empty(harness.Observations.GroupListings());
        Assert.Empty(harness.Observations.GroupUpdates());

        harness.Send(EntityPacket(2));
        harness.RunPass(tick: 2);
        Assert.Equal(4, Assert.Single(harness.Observations.EntityArrivals()).Sequence);
    }

    [Fact]
    public void RestoredHook_Should_ClearEverything_LikeClear()
    {
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);
        harness.Send(EntityPacket(2));

        EnumHandling handling = EnumHandling.PassThrough;
        harness.OnRestored!("atlas:rollback:restored", ref handling, new TreeAttribute());

        Assert.Empty(harness.Observations.EntityArrivals());
        Assert.Equal(0, harness.Pending);
    }

    [Fact]
    public void Detach_Should_ParkOnceMore_UnregisterBothListeners_AndLeaveTheDataReadable()
    {
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 3);
        harness.Send(EntityPacket(2));
        harness.Tick = 4;

        harness.Observations.Detach();

        harness.Api.Event.Received(1).UnregisterGameTickListener(Harness.ListenerId);
        Assert.Equal(ExpectedHookUnregistrations, harness.UnregisteredHooks());
        Assert.Equal(0, harness.Pending);

        // What a kicked player received is still there, the last packets included, stamped by
        // the last park.
        Assert.Equal(
            [(1L, 3, 0), (2L, 4, 1)],
            harness.Observations.EntityArrivals().Select(e => (e.EntityId, e.Tick, e.Sequence)).ToArray());
    }

    [Fact]
    public void RestoredHook_Should_LeaveAGonePlayersObservationsAlone_When_ItStillFires()
    {
        // 1.21.x has no way to unregister the hook, so after Detach it can still be called.
        var harness = new Harness();
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);
        harness.Observations.Detach();

        EnumHandling handling = EnumHandling.PassThrough;
        harness.OnRestored!("atlas:rollback:restored", ref handling, new TreeAttribute());

        Assert.True(harness.Observations.HasReceivedEntity(1));
    }

    [Fact]
    public void Detach_Should_BeIdempotent()
    {
        var harness = new Harness();

        harness.Observations.Detach();
        harness.Observations.Detach();

        harness.Api.Event.Received(1).UnregisterGameTickListener(Arg.Any<long>());
        Assert.Equal(ExpectedHookUnregistrations, harness.UnregisteredHooks());
    }

    [Fact]
    public void Listener_Should_DropPacketsOfKindsItDoesNotDecode_AndStillNumberThem()
    {
        // The particle provider would throw on a decode, so a packet that reached the decoder
        // would fail the read: it carries the particle sub-message under id 60 (bulk entity
        // attributes), which the listener drops by id the moment it dequeues it.
        var harness = new Harness();
        harness.Api.ClassRegistry.CreateParticlePropertyProvider(Arg.Any<string>()).Throws(new InvalidOperationException("would throw if decoded"));

        harness.Send(new Packet_Server { Id = 60, SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "nope", Data = [] } });
        harness.Send(new Packet_Server { Id = 51, EntityPosition = new Packet_EntityPosition { EntityId = 1 } });
        harness.Send(EntityPacket(7));
        harness.RunPass(tick: 2);

        Assert.Equal(0, harness.Pending);
        ReceivedEntity arrival = Assert.Single(harness.Observations.EntityArrivals());

        // Numbered like every other packet the player received: two dropped ones came first.
        Assert.Equal((7L, 2, 2), (arrival.EntityId, arrival.Tick, arrival.Sequence));
        Assert.Empty(harness.Observations.Particles());
    }

    [Fact]
    public void Listener_Should_ParkEveryKindItDecodes_UnderItsOwnId()
    {
        var harness = new Harness();

        harness.Send(new Packet_Server { Id = 8, Chatline = new Packet_ChatLine { Message = "hello", Groupid = 0, ChatType = (int)EnumChatType.Notification } });
        harness.Send(EntityPacket(1));
        harness.Send(new Packet_Server { Id = 34, EntitySpawn = new Packet_EntitySpawn { Entity = [Entity(2)], EntityCount = 1, EntityLength = 1 } });
        harness.Send(new Packet_Server { Id = 40, Entities = new Packet_Entities { Entities = [Entity(3)], EntitiesCount = 1, EntitiesLength = 1 } });
        harness.Send(PlayerDataPacket("uid-b", clientId: 2));
        harness.Send(GroupListingPacket("g"));
        harness.Send(GroupUpdatePacket("g2"));
        harness.Send(new Packet_Server { Id = 52, HighlightBlocks = new Packet_HighlightBlocks { Slotid = 4, Blocks = [] } });
        harness.Send(new Packet_Server { Id = 55, CustomPacket = new Packet_CustomPacket { ChannelId = 1, MessageId = 1, Data = [] } });
        harness.RunPass(tick: 3);

        Assert.Equal(["hello"], harness.Observations.ChatLines());
        Assert.Equal([1L, 2L, 3L], harness.Observations.EntityArrivals().Select(e => e.EntityId).ToArray());
        Assert.Single(harness.Observations.PlayerData());
        Assert.Single(harness.Observations.GroupListings());
        Assert.Single(harness.Observations.GroupUpdates());
        Assert.Empty(harness.Observations.Highlights(4));
    }

    [Fact]
    public void Read_Should_ReportAMessageWhoseIdCannotBeRead_AndKeepTheOnesAroundIt()
    {
        // The id key, then a varint longer than any 32-bit value: no id to classify it by, so it
        // is parked and the read decodes it, as before the filter, and fails.
        var harness = new Harness();

        harness.Send(EntityPacket(1));
        harness.SendBytes([0xD0, 0x05, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        harness.Send(EntityPacket(3));
        harness.RunPass(tick: 4);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => harness.Observations.EntityArrivals());

        Assert.Contains("envelope could not be read", failure.Message);
        Assert.Contains("Tick 4", failure.Message);
        Assert.Contains("Sequence 1", failure.Message);
        Assert.Equal([1L, 3L], harness.Observations.EntityArrivals().Select(e => e.EntityId).ToArray());
    }

    [Fact]
    public void Read_Should_DecodeTheIdentificationPacket_WhoseIdIsOmittedOnTheWire_WithoutAFailure()
    {
        var harness = new Harness();

        harness.Send(new Packet_Server { Id = 1, Identification = new Packet_ServerIdentification { ServerName = "atlas" } });
        harness.Send(EntityPacket(1));
        harness.RunPass(tick: 1);

        Assert.Equal(1, Assert.Single(harness.Observations.EntityArrivals()).Sequence);
    }

    private static Packet_Entity Entity(long id) => new() { EntityId = id, EntityType = "chicken-rooster", SimulationRange = 32 };

    private static Packet_Server EntityPacket(long id) => new() { Id = 33, Entity = Entity(id) };

    private static Packet_Server DespawnPacket(params (long Id, EnumDespawnReason Reason)[] despawns)
    {
        var despawn = new Packet_EntityDespawn();
        despawn.SetEntityId([.. despawns.Select(d => d.Id)]);
        despawn.SetDespawnReason([.. despawns.Select(d => (int)d.Reason)]);
        despawn.SetDeathDamageSource([.. despawns.Select(_ => (int)EnumDamageSource.Unknown)]);
        return new Packet_Server { Id = 36, EntityDespawn = despawn };
    }

    private static Packet_Server PlayerDataPacket(string uid, int clientId)
        => new() { Id = 41, PlayerData = new Packet_PlayerData { PlayerUID = uid, PlayerName = uid, ClientId = clientId, EntityId = 99 } };

    private static Packet_Server GroupListingPacket(string name)
        => new()
        {
            Id = 49,
            PlayerGroups = new Packet_PlayerGroups
            {
                Groups = [new Packet_PlayerGroup { Uid = 1, Name = name, Owneruid = OwnUid, Membership = 3 }],
                GroupsCount = 1,
                GroupsLength = 1,
            },
        };

    private static Packet_Server GroupUpdatePacket(string name)
        => new() { Id = 50, PlayerGroup = new Packet_PlayerGroup { Uid = 2, Name = name, Owneruid = OwnUid, Membership = 3 } };

    /// <summary>One <see cref="ClientObservations"/> over a queue standing in for the dummy
    /// connection's receive buffer and a server API substitute that keeps the listeners it was
    /// handed, so a test can run "a pass" by calling the registered tick listener.</summary>
    private sealed class Harness
    {
        public const long ListenerId = 4242;

        private readonly Queue<NetIncomingMessage> _queue = new();

        public Harness()
        {
            Api = Substitute.For<ICoreServerAPI>();
            Api.Event
                .RegisterGameTickListener(Arg.Any<Action<float>>(), Arg.Any<Action<Exception>>(), Arg.Any<int>(), Arg.Any<int>())
                .Returns(call =>
                {
                    OnPass = call.ArgAt<Action<float>>(0);
                    OnError = call.ArgAt<Action<Exception>>(1);
                    TickIntervalMs = call.ArgAt<int>(2);
                    return ListenerId;
                });
            Api.Event
                .When(events => events.RegisterEventBusListener(Arg.Any<EventBusListenerDelegate>(), Arg.Any<double>(), Arg.Any<string>()))
                .Do(call => OnRestored = call.ArgAt<EventBusListenerDelegate>(0));

            Observations = new ClientObservations(Api, Read, () => Tick, OwnUid);
        }

        public ICoreServerAPI Api { get; }

        public ClientObservations Observations { get; }

        public Action<float>? OnPass { get; private set; }

        public Action<Exception>? OnError { get; private set; }

        public EventBusListenerDelegate? OnRestored { get; private set; }

        public int TickIntervalMs { get; private set; }

        public int Tick { get; set; }

        public int Pending => _queue.Count;

        public int UnregisteredHooks()
            => Api.Event.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "UnregisterEventBusListener");

        public void Send(Packet_Server packet) => SendBytes(Packet_ServerSerializer.SerializeToBytes(packet));

        public void SendBytes(byte[] bytes)
            => _queue.Enqueue(new NetIncomingMessage { message = bytes, messageLength = bytes.Length });

        public void RunPass(int tick)
        {
            Tick = tick;
            OnPass!(0.033f);
        }

        private NetIncomingMessage? Read() => _queue.Count > 0 ? _queue.Dequeue() : null;
    }
}
