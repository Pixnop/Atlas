using Atlas.Internal.Player;

namespace Atlas.Pure.Tests.Player;

/// <summary>Tests for the packet id peek behind the drop-at-dequeue filter of
/// <see cref="ClientObservations"/>, over bytes produced by the engine's own serializer (the exact
/// shapes the server hands the dummy connection). The same wire layout is pinned on every
/// supported install by <c>EngineContractTests</c>.</summary>
public class ServerPacketIdTests
{
    private static readonly int[] DecodedIds = [8, 33, 34, 40, 41, 49, 50, 52, 55, 61];

    [Fact]
    public void TryRead_Should_ReadTheId_And_ShouldPark_Should_BeTrue_ForEveryKindObservationsDecodes()
    {
        foreach (Packet_Server packet in DecodedKinds())
        {
            byte[] bytes = Packet_ServerSerializer.SerializeToBytes(packet);

            Assert.True(ServerPacketId.TryRead(bytes, bytes.Length, out int id), $"id {packet.Id}");
            Assert.Equal(packet.Id, id);
            Assert.True(ServerPacketId.IsDecoded(id), $"id {id}");
            Assert.True(ServerPacketId.ShouldPark(bytes, bytes.Length), $"id {id}");
        }
    }

    [Fact]
    public void IsDecoded_Should_BeTrueForExactlyTheIdsTheDrainDecodes()
    {
        Assert.Equal(DecodedIds, Enumerable.Range(-5, 400).Where(ServerPacketId.IsDecoded).ToArray());
        Assert.Equal(DecodedIds, DecodedKinds().Select(packet => packet.Id).ToArray());
    }

    [Fact]
    public void ShouldPark_Should_BeFalse_ForEveryOtherKindTheEngineSendsAPlayer()
    {
        // The ids the engine sends over TCP (decompiled on 1.21.7, 1.22.3 and 1.22.7), none of
        // them one the drain decodes. Only the id matters to the peek, so a payload-less packet
        // stands for each; the entity position and a long attribute list stand for the big ones.
        int[] others =
        [
            2, 3, 4, 5, 6, 7, 9, 10, 11, 13, 17, 18, 19, 21, 28, 29, 30, 31, 32, 36, 37, 38, 42, 44, 45, 46, 48, 51, 53, 56,
            57, 58, 60, 62, 64, 65, 66, 67, 68, 69, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83,
        ];
        Assert.DoesNotContain(others, ServerPacketId.IsDecoded);

        foreach (int id in others)
        {
            byte[] bytes = Packet_ServerSerializer.SerializeToBytes(new Packet_Server { Id = id });

            Assert.True(ServerPacketId.TryRead(bytes, bytes.Length, out int read), $"id {id}");
            Assert.Equal(id, read);
            Assert.False(ServerPacketId.ShouldPark(bytes, bytes.Length), $"id {id}");
        }

        byte[] position = Packet_ServerSerializer.SerializeToBytes(new Packet_Server
        {
            Id = 51,
            EntityPosition = new Packet_EntityPosition { EntityId = 4_000_000_001L, X = 1, Y = 2, Z = 3 },
        });
        Assert.False(ServerPacketId.ShouldPark(position, position.Length));
    }

    [Fact]
    public void TryRead_Should_StartTheWireWithTheIdField_On_TheEnginesOwnBytes()
    {
        // The layout the peek relies on: the serializer writes Id first, as key 720 (D0 05) and a
        // varint. If an engine moved it, this and the contract test on every install fail first.
        byte[] bytes = Packet_ServerSerializer.SerializeToBytes(
            new Packet_Server { Id = 33, Entity = new Packet_Entity { EntityId = 1, EntityType = "chicken-hen" } });

        Assert.Equal([0xD0, 0x05, 33], bytes[..3]);
    }

    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(300)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(70000)]
    [InlineData(int.MaxValue)]
    public void TryRead_Should_ReadMultiByteIds(int id)
    {
        byte[] bytes = Packet_ServerSerializer.SerializeToBytes(new Packet_Server { Id = id });

        Assert.True(ServerPacketId.TryRead(bytes, bytes.Length, out int read));
        Assert.Equal(id, read);
    }

    [Fact]
    public void ShouldPark_Should_BeTrue_ForTheIdentificationPacket_WhoseIdTheSerializerOmits()
    {
        // Id 1 is the field's default, so the serializer leaves it out: nothing to read, and the
        // message is parked rather than guessed at.
        byte[] bytes = Packet_ServerSerializer.SerializeToBytes(new Packet_Server
        {
            Id = 1,
            Identification = new Packet_ServerIdentification { ServerName = "atlas", NetworkVersion = "1" },
        });

        Assert.False(ServerPacketId.TryRead(bytes, bytes.Length, out int id));
        Assert.Equal(0, id);
        Assert.True(ServerPacketId.ShouldPark(bytes, bytes.Length));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xD0 })]
    [InlineData(new byte[] { 0xD0, 0x05 })]
    [InlineData(new byte[] { 0xD0, 0x05, 0x80 })]
    [InlineData(new byte[] { 0xD0, 0x05, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(new byte[] { 0x08, 0x01 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0xD0, 0x06, 0x21 })]
    [InlineData(new byte[] { 0x50, 0x05, 0x21 })]
    public void ShouldPark_Should_BeTrue_WhenTheIdCannotBeRead(byte[] bytes)
    {
        Assert.False(ServerPacketId.TryRead(bytes, bytes.Length, out int id));
        Assert.Equal(0, id);
        Assert.True(ServerPacketId.ShouldPark(bytes, bytes.Length));
    }

    [Fact]
    public void TryRead_Should_StopAtTheMessageLength_NotAtTheEndOfAPooledBuffer()
    {
        // The engine's buffers are larger than the message they carry.
        byte[] buffer = [0xD0, 0x05, 8, 0xFF, 0xFF, 0xFF];

        Assert.True(ServerPacketId.TryRead(buffer, 3, out int id));
        Assert.Equal(8, id);

        // A varint that only terminates past the declared length is not an id.
        byte[] cut = [0xD0, 0x05, 0x81, 0x01];
        Assert.False(ServerPacketId.TryRead(cut, 3, out _));
        Assert.True(ServerPacketId.ShouldPark(cut, 3));

        // A length the buffer cannot back never throws.
        Assert.False(ServerPacketId.TryRead([0xD0, 0x05, 8], 99, out _));
        Assert.False(ServerPacketId.TryRead([0xD0, 0x05, 8], -1, out _));
    }

    /// <summary>One packet of each decoded kind, with the id the engine sends it under.</summary>
    private static IEnumerable<Packet_Server> DecodedKinds()
    {
        yield return new Packet_Server { Id = 8, Chatline = new Packet_ChatLine { Message = "hello", Groupid = 0 } };
        yield return new Packet_Server { Id = 33, Entity = new Packet_Entity { EntityId = 1, EntityType = "chicken-hen" } };
        yield return new Packet_Server
        {
            Id = 34,
            EntitySpawn = new Packet_EntitySpawn { Entity = [new Packet_Entity { EntityId = 2 }], EntityCount = 1, EntityLength = 1 },
        };
        yield return new Packet_Server
        {
            Id = 40,
            Entities = new Packet_Entities { Entities = [new Packet_Entity { EntityId = 3 }], EntitiesCount = 1, EntitiesLength = 1 },
        };
        yield return new Packet_Server { Id = 41, PlayerData = new Packet_PlayerData { PlayerUID = "uid", PlayerName = "bob", ClientId = 1 } };
        yield return new Packet_Server
        {
            Id = 49,
            PlayerGroups = new Packet_PlayerGroups { Groups = [new Packet_PlayerGroup { Uid = 1, Name = "g" }], GroupsCount = 1, GroupsLength = 1 },
        };
        yield return new Packet_Server { Id = 50, PlayerGroup = new Packet_PlayerGroup { Uid = 2, Name = "g2" } };
        yield return new Packet_Server { Id = 52, HighlightBlocks = new Packet_HighlightBlocks { Slotid = 7, Blocks = [1, 2, 3] } };
        yield return new Packet_Server { Id = 55, CustomPacket = new Packet_CustomPacket { ChannelId = 3, MessageId = 1, Data = [9] } };
        yield return new Packet_Server
        {
            Id = 61,
            SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "SimpleParticleProperties", Data = [] },
        };
    }
}
