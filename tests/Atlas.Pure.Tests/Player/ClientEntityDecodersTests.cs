using Atlas.Internal.Player;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Atlas.Pure.Tests.Player;

/// <summary>Tests for the entity, player-data and player-group decoders behind
/// <see cref="ClientObservations"/>, over bytes produced by the engine's own serializer (the exact
/// shapes the server sends, null tails of the engine's growing arrays included): the drain and the
/// stamping are in <see cref="ClientObservationsDrainTests"/>, the live paths in the E2E suite.</summary>
public class ClientEntityDecodersTests
{
    [Theory]
    [InlineData("chicken-rooster", "chicken-rooster")]
    [InlineData("game:chicken-rooster", "chicken-rooster")]
    [InlineData("player", "player")]
    [InlineData("mymod:thing", "mymod:thing")]
    public void DecodeEntity_Should_ReadTheTypeInItsShortForm_Whatever_FormTheSenderUsed(string wire, string expected)
    {
        Packet_Server received = RoundTrip(new Packet_Server
        {
            Id = 33,
            Entity = new Packet_Entity { EntityId = 4_000_000_001L, EntityType = wire, SimulationRange = 32 },
        });

        ReceivedEntity decoded = ClientObservations.DecodeEntity(received.Entity, EntityArrivalPath.TrackedRange, tick: 12, sequence: 7);

        Assert.Equal(new ReceivedEntity(4_000_000_001L, expected, EntityArrivalPath.TrackedRange, 12, 7), decoded);
    }

    [Fact]
    public void DecodeEntity_Should_ReportAnEmptyType_When_ThePacketCarriesNone()
    {
        ReceivedEntity decoded = ClientObservations.DecodeEntity(new Packet_Entity { EntityId = 5 }, EntityArrivalPath.Spawn, 1, 2);

        Assert.Equal(string.Empty, decoded.EntityType);
        Assert.Equal(5, decoded.EntityId);
    }

    [Fact]
    public void DecodeEntities_Should_StopAtTheCount_When_TheArrayHasANullTail()
    {
        // The engine's spawn packet is built from an array sized for the whole batch, and its
        // count says how many entries are real: the rest of the array is null.
        var packet = new Packet_EntitySpawn
        {
            Entity = [Entity(10, "chicken-rooster"), Entity(11, "game:chicken-hen"), null!, null!],
            EntityCount = 2,
            EntityLength = 2,
        };

        ReceivedEntity[] decoded = ClientObservations.DecodeEntities(packet.Entity, packet.EntityCount, EntityArrivalPath.Spawn, tick: 3, sequence: 9);

        Assert.Equal(
            [
                new ReceivedEntity(10, "chicken-rooster", EntityArrivalPath.Spawn, 3, 9),
                new ReceivedEntity(11, "chicken-hen", EntityArrivalPath.Spawn, 3, 9),
            ],
            decoded);
    }

    [Fact]
    public void DecodeEntities_Should_ReadTheEntriesTheSerializerKept_When_ASpawnPacketRoundTrips()
    {
        var sent = new Packet_EntitySpawn { Entity = [Entity(1, "player"), Entity(2, "chicken-rooster"), null!], EntityCount = 2, EntityLength = 2 };

        Packet_Server received = RoundTrip(new Packet_Server { Id = 34, EntitySpawn = sent });
        ReceivedEntity[] decoded = ClientObservations.DecodeEntities(
            received.EntitySpawn.Entity, received.EntitySpawn.EntityCount, EntityArrivalPath.Spawn, 0, 0);

        Assert.Equal([1L, 2L], decoded.Select(e => e.EntityId).ToArray());
        Assert.Equal(["player", "chicken-rooster"], decoded.Select(e => e.EntityType).ToArray());
    }

    [Fact]
    public void DecodeEntities_Should_ReadTheJoinList_When_APacket40RoundTrips()
    {
        var sent = new Packet_Entities { Entities = [Entity(7, "player"), null!, null!], EntitiesCount = 1, EntitiesLength = 1 };

        Packet_Server received = RoundTrip(new Packet_Server { Id = 40, Entities = sent });
        ReceivedEntity[] decoded = ClientObservations.DecodeEntities(
            received.Entities.Entities, received.Entities.EntitiesCount, EntityArrivalPath.JoinList, 4, 5);

        Assert.Equal(new ReceivedEntity(7, "player", EntityArrivalPath.JoinList, 4, 5), Assert.Single(decoded));
    }

    [Fact]
    public void DecodeEntities_Should_SkipNullEntriesAndClampTheCount_When_TheArrayIsShorterThanTheCountSays()
    {
        Packet_Entity[] entries = [Entity(1, "player"), null!, Entity(3, "player")];

        ReceivedEntity[] decoded = ClientObservations.DecodeEntities(entries, count: 10, EntityArrivalPath.JoinList, 0, 0);

        Assert.Equal([1L, 3L], decoded.Select(e => e.EntityId).ToArray());
    }

    [Fact]
    public void DecodeEntities_Should_ReturnNothing_When_ThePacketHasNoArrayOrNoCount()
    {
        Assert.Empty(ClientObservations.DecodeEntities(null, count: 3, EntityArrivalPath.Spawn, 0, 0));
        Assert.Empty(ClientObservations.DecodeEntities([Entity(1, "player")], count: 0, EntityArrivalPath.Spawn, 0, 0));
    }

    [Fact]
    public void DecodePlayerData_Should_FlagTheDepartureMarker_When_TheClientIdIsMinus99()
    {
        Packet_Server received = RoundTrip(new Packet_Server
        {
            Id = 41,
            PlayerData = new Packet_PlayerData { PlayerUID = "uid-b", ClientId = -99 },
        });

        ReceivedPlayerData decoded = ClientObservations.DecodePlayerData(received.PlayerData, "uid-a", tick: 8, sequence: 2);

        Assert.True(decoded.IsDeparture);
        Assert.False(decoded.IsSelf);
        Assert.Equal("uid-b", decoded.PlayerUid);
        Assert.Equal(-99, decoded.ClientId);
        Assert.Equal(0, decoded.EntityId);
        Assert.Equal((8, 2), (decoded.Tick, decoded.Sequence));
    }

    [Fact]
    public void DecodePlayerData_Should_LiftTheIdentityFields_When_ThePacketDescribesAnotherPlayer()
    {
        Packet_Server received = RoundTrip(new Packet_Server
        {
            Id = 41,
            PlayerData = new Packet_PlayerData
            {
                PlayerUID = "uid-b",
                PlayerName = "Bea",
                EntityId = 123_456_789_012L,
                ClientId = 4,
                GameMode = (int)EnumGameMode.Creative,
            },
        });

        ReceivedPlayerData decoded = ClientObservations.DecodePlayerData(received.PlayerData, "uid-a", 1, 1);

        Assert.Equal(new ReceivedPlayerData("uid-b", "Bea", 123_456_789_012L, 4, EnumGameMode.Creative, false, false, 1, 1), decoded);
    }

    [Fact]
    public void DecodePlayerData_Should_FlagSelf_When_TheUidIsTheReceivingPlayersOwn()
    {
        var packet = new Packet_PlayerData { PlayerUID = "uid-a", ClientId = 3 };

        Assert.True(ClientObservations.DecodePlayerData(packet, "uid-a", 0, 0).IsSelf);
        Assert.False(ClientObservations.DecodePlayerData(packet, "UID-A", 0, 0).IsSelf);
    }

    [Fact]
    public void DecodePlayerData_Should_ReportEmptyStrings_When_ThePacketCarriesNoUidOrName()
    {
        // The first packet a joining player gets about itself has no name.
        ReceivedPlayerData decoded = ClientObservations.DecodePlayerData(new Packet_PlayerData { ClientId = 1 }, "uid-a", 0, 0);

        Assert.Equal(string.Empty, decoded.PlayerUid);
        Assert.Equal(string.Empty, decoded.PlayerName);
        Assert.False(decoded.IsSelf);
    }

    [Fact]
    public void DecodeGroupListing_Should_KeepTheServersOrder_And_StopAtTheCount_When_TheArrayHasANullTail()
    {
        var sent = new Packet_PlayerGroups
        {
            Groups =
            [
                Group(10, "first", "uid-a", (int)EnumPlayerGroupMemberShip.Owner),
                Group(11, "second", "uid-c", (int)EnumPlayerGroupMemberShip.Member),
                Group(12, "third", "uid-d", (int)EnumPlayerGroupMemberShip.Op),
                null!,
            ],
            GroupsCount = 3,
            GroupsLength = 3,
        };

        Packet_Server received = RoundTrip(new Packet_Server { Id = 49, PlayerGroups = sent });
        ReceivedGroupListing listing = ClientObservations.DecodeGroupListing(received.PlayerGroups, tick: 6, sequence: 4);

        Assert.Equal((6, 4), (listing.Tick, listing.Sequence));
        Assert.Equal(["first", "second", "third"], listing.Groups.Select(g => g.Name).ToArray());
        Assert.Equal(new ReceivedPlayerGroup(10, "first", "uid-a", EnumPlayerGroupMemberShip.Owner), listing.Groups[0]);
        Assert.Equal(EnumPlayerGroupMemberShip.Member, listing.Groups[1].Membership);
        Assert.Equal(EnumPlayerGroupMemberShip.Op, listing.Groups[2].Membership);
    }

    [Fact]
    public void DecodeGroupListing_Should_ReturnAnEmptyListing_When_TheServerSendsNoGroups()
    {
        // Every join sends one: the empty listing is the positive control for the others.
        Packet_Server received = RoundTrip(new Packet_Server { Id = 49, PlayerGroups = new Packet_PlayerGroups() });

        ReceivedGroupListing listing = ClientObservations.DecodeGroupListing(received.PlayerGroups, 0, 0);

        Assert.Empty(listing.Groups);
    }

    [Fact]
    public void DecodeGroupListing_Should_NotExposeAWritableArray()
    {
        var packet = new Packet_PlayerGroups { Groups = [Group(1, "g", "o", 1)], GroupsCount = 1, GroupsLength = 1 };

        IReadOnlyList<ReceivedPlayerGroup> groups = ClientObservations.DecodeGroupListing(packet, 0, 0).Groups;

        Assert.False(groups is ReceivedPlayerGroup[]);
    }

    [Fact]
    public void DecodeGroupUpdate_Should_LiftTheOneGroup_When_APacket50RoundTrips()
    {
        Packet_Server received = RoundTrip(new Packet_Server
        {
            Id = 50,
            PlayerGroup = Group(21, "builders", "uid-a", (int)EnumPlayerGroupMemberShip.Owner),
        });

        ReceivedGroupUpdate update = ClientObservations.DecodeGroupUpdate(received.PlayerGroup, tick: 9, sequence: 3);

        Assert.Equal(
            new ReceivedGroupUpdate(9, 3, new ReceivedPlayerGroup(21, "builders", "uid-a", EnumPlayerGroupMemberShip.Owner)),
            update);
    }

    [Fact]
    public void DecodeGroup_Should_ReportEmptyStrings_When_ThePacketCarriesNoNameOrOwner()
    {
        ReceivedPlayerGroup group = ClientObservations.DecodeGroup(new Packet_PlayerGroup { Uid = 5 });

        Assert.Equal(new ReceivedPlayerGroup(5, string.Empty, string.Empty, EnumPlayerGroupMemberShip.None), group);
    }

    private static Packet_Entity Entity(long id, string type)
        => new() { EntityId = id, EntityType = type, SimulationRange = 32 };

    private static Packet_PlayerGroup Group(int uid, string name, string owner, int membership)
        => new() { Uid = uid, Name = name, Owneruid = owner, Membership = membership };

    /// <summary>The exact bytes the server hands the dummy connection, decoded the way the drain
    /// decodes them.</summary>
    private static Packet_Server RoundTrip(Packet_Server sent)
    {
        byte[] bytes = Packet_ServerSerializer.SerializeToBytes(sent);
        return Packet_ServerSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_Server());
    }
}
