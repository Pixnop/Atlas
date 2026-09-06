using Atlas.Internal.Player;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Atlas.Pure.Tests.Player;

/// <summary>Tests for the packet decoders behind <see cref="ClientObservations"/>, over bytes
/// produced by the engine's own packers and serializer (the exact shapes the server sends): the
/// drain loop and the live channel registry are exercised by the E2E suite.</summary>
public class ClientObservationsTests
{
    [Fact]
    public void DecodeHighlight_Should_PairEachPositionWithItsColor_When_OneColorPerPositionIsSent()
    {
        var positions = new List<BlockPos> { new(1, 2, 3), new(4, 5, 6, 2) };
        int[] colors = [ColorUtil.ColorFromRgba(255, 0, 0, 128), ColorUtil.ColorFromRgba(0, 255, 0, 255)];

        (int slot, HighlightedBlock[] blocks) = ClientObservations.DecodeHighlight(HighlightPacket(7, positions, colors));

        Assert.Equal(7, slot);
        Assert.Equal(positions, blocks.Select(b => b.Pos).ToList());
        Assert.Equal(colors, blocks.Select(b => b.Color).ToArray());
        Assert.Equal(new Rgba(255, 0, 0, 128), blocks[0].Rgba);
        Assert.Equal(new Rgba(0, 255, 0, 255), blocks[1].Rgba);
        Assert.Equal(2, blocks[1].Pos.dimension);
    }

    [Fact]
    public void DecodeHighlight_Should_UseTheFirstColorForEveryPosition_When_FewerColorsThanPositionsAreSent()
    {
        var positions = new List<BlockPos> { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9) };
        int[] colors = [11, 22];

        (_, HighlightedBlock[] blocks) = ClientObservations.DecodeHighlight(HighlightPacket(1, positions, colors));

        Assert.Equal([11, 11, 11], blocks.Select(b => b.Color).ToArray());
    }

    [Fact]
    public void DecodeHighlight_Should_ReportZeroColor_When_NoColorsAreSent()
    {
        (_, HighlightedBlock[] blocks) = ClientObservations.DecodeHighlight(
            HighlightPacket(1, [new(1, 2, 3)], colors: null));

        Assert.Equal(0, Assert.Single(blocks).Color);
    }

    [Fact]
    public void DecodeHighlight_Should_ReturnNoBlocks_When_ThePacketCarriesNoPositions()
    {
        (int slot, HighlightedBlock[] blocks) = ClientObservations.DecodeHighlight(
            HighlightPacket(7, [], colors: null));

        Assert.Equal(7, slot);
        Assert.Empty(blocks);
    }

    [Fact]
    public void DecodeParticles_Should_LiftTheSimpleProviderFields_When_TheServerSentSimpleParticles()
    {
        var sent = new SimpleParticleProperties(
            5f, 5f, ColorUtil.ToRgba(200, 255, 10, 20), new Vec3d(10, 20, 30), new Vec3d(11, 21, 31), new Vec3f(0.1f, 0.2f, 0.3f), new Vec3f(1, 1, 1));

        SpawnedParticles decoded = ClientObservations.DecodeParticles(
            ParticlePacket("simple", sent), _ => new SimpleParticleProperties(), world: null!);

        Assert.Equal("simple", decoded.ProviderClassName);
        Assert.IsType<SimpleParticleProperties>(decoded.Provider);
        Assert.Equal(new Vec3d(10, 20, 30), decoded.Position);
        Assert.Equal(new Vec3f(0.1f, 0.2f, 0.3f), decoded.Velocity);
        Assert.Equal(5f, decoded.Quantity);
        Assert.Equal(sent.Color, decoded.Color);
        Assert.Equal(new Rgba(255, 10, 20, 200), decoded.Rgba);
        Assert.Equal(new Vec3d(1, 1, 1), ((SimpleParticleProperties)decoded.Provider).AddPos);
    }

    [Fact]
    public void DecodeParticles_Should_ReadTheProvidersOwnValues_When_TheProviderIsNotSimple()
    {
        var sent = new FixedParticles { Position = new Vec3d(1, 2, 3) };

        SpawnedParticles decoded = ClientObservations.DecodeParticles(
            ParticlePacket("fixed", sent), name => name == "fixed" ? new FixedParticles() : throw new InvalidOperationException(name), world: null!);

        Assert.Equal(new Vec3d(1, 2, 3), decoded.Position);
        Assert.Equal(new Vec3f(0, 1, 0), decoded.Velocity);
        Assert.Equal(3f, decoded.Quantity);
        Assert.Equal(0, decoded.Color);
    }

    [Fact]
    public void ResolveChannelMessage_Should_ThrowNamingTheChannel_When_NoChannelIsRegistered()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ClientObservations.ResolveChannelMessage(null, "caminus", typeof(FakeMessage)));

        Assert.Contains("'caminus'", ex.Message);
        Assert.Contains("RegisterChannel", ex.Message);
    }

    [Fact]
    public void ResolveChannelMessage_Should_MatchByFullTypeName_When_TheChannelRegisteredTheType()
    {
        IServerNetworkChannel channel = new FakeServerChannel(channelId: 3, typeof(FakeMessage), typeof(OtherMessage));

        Assert.Equal((3, 1), ClientObservations.ResolveChannelMessage(channel, "atlasfixture", typeof(OtherMessage)));
    }

    [Fact]
    public void ResolveChannelMessage_Should_ThrowListingRegisteredTypes_When_TheTypeIsNotRegistered()
    {
        IServerNetworkChannel channel = new FakeServerChannel(channelId: 3, typeof(FakeMessage));

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ClientObservations.ResolveChannelMessage(channel, "atlasfixture", typeof(OtherMessage)));

        Assert.Contains(typeof(OtherMessage).FullName!, ex.Message);
        Assert.Contains(typeof(FakeMessage).FullName!, ex.Message);
        Assert.Contains("RegisterMessageType", ex.Message);
    }

    [Fact]
    public void Rgba_Should_DecodeBothEngineLayouts()
    {
        Assert.Equal(new Rgba(1, 2, 3, 4), Rgba.FromRgba(ColorUtil.ColorFromRgba(1, 2, 3, 4)));
        Assert.Equal(new Rgba(1, 2, 3, 4), Rgba.FromArgb(ColorUtil.ToRgba(4, 1, 2, 3)));
    }

    [Fact]
    public void Packet_Should_RoundTripThroughTheEngineSerializer_When_DecodedFromBytes()
    {
        // The exact bytes the server hands the dummy connection: a full Packet_Server envelope.
        Packet_Server sent = new() { Id = 52, HighlightBlocks = HighlightPacket(7, [new(1, 2, 3)], [9]) };
        byte[] bytes = Packet_ServerSerializer.SerializeToBytes(sent);

        Packet_Server received = Packet_ServerSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_Server());

        Assert.Null(received.SpawnParticles);
        (int slot, HighlightedBlock[] blocks) = ClientObservations.DecodeHighlight(received.HighlightBlocks);
        Assert.Equal(7, slot);
        Assert.Equal(new BlockPos(1, 2, 3), Assert.Single(blocks).Pos);
    }

    /// <summary>Builds the packet exactly as <c>ServerMain.SendHighlightBlocksPacket</c> does.</summary>
    private static Packet_HighlightBlocks HighlightPacket(int slot, List<BlockPos> positions, int[]? colors)
    {
        var packet = new Packet_HighlightBlocks { Slotid = slot, Blocks = BlockTypeNet.PackBlocksPositions(positions) };
        if (colors != null)
        {
            packet.SetColors(colors);
        }

        return packet;
    }

    /// <summary>Builds the packet exactly as <c>ServerMain.SpawnParticles</c> does.</summary>
    private static Packet_SpawnParticles ParticlePacket(string className, IParticlePropertiesProvider provider)
    {
        using var stream = new MemoryStream();
        provider.ToBytes(new BinaryWriter(stream));
        return new Packet_SpawnParticles { ParticlePropertyProviderClassName = className, Data = stream.ToArray() };
    }

    [ProtoContract]
    private sealed class FakeMessage
    {
        [ProtoMember(1)]
        public string Text { get; set; } = string.Empty;
    }

    [ProtoContract]
    private sealed class OtherMessage
    {
        [ProtoMember(1)]
        public int Value { get; set; }
    }

    /// <summary>The engine's own server channel shape (<c>NetworkChannelBase</c>, the type the
    /// wire-id fields live on), registered with the real registration path.</summary>
    private sealed class FakeServerChannel : NetworkChannelBase, IServerNetworkChannel
    {
        public FakeServerChannel(int channelId, params Type[] messageTypes)
            : base(channelId, "fake")
        {
            foreach (Type type in messageTypes)
            {
                ((INetworkChannel)this).RegisterMessageType(type);
            }
        }

        public IServerNetworkChannel SetMessageHandler<T>(NetworkClientMessageHandler<T> handler) => throw new NotSupportedException();

        public void SendPacket<T>(T message, params IServerPlayer[] players) => throw new NotSupportedException();

        public void SendPacket<T>(T message, byte[] data, params IServerPlayer[] players) => throw new NotSupportedException();

        public void BroadcastPacket<T>(T message, params IServerPlayer[] exceptPlayers) => throw new NotSupportedException();

        IServerNetworkChannel IServerNetworkChannel.RegisterMessageType(Type type) => throw new NotSupportedException();

        IServerNetworkChannel IServerNetworkChannel.RegisterMessageType<T>() => throw new NotSupportedException();
    }

    /// <summary>A minimal non-simple provider with a fixed position, velocity and quantity, so
    /// the generic decode path (the provider's own values, no color) is observable.</summary>
    private sealed class FixedParticles : IParticlePropertiesProvider
    {
        public Vec3d Position { get; set; } = new();

        public bool IgnoreUserConfig => false;

        public bool Async => false;

        public float ParentVelocityWeight => 0;

        public bool DieInLiquid => false;

        public bool SwimOnLiquid => false;

        public float Bounciness => 0;

        public bool DieInAir => false;

        public bool DieOnRainHeightmap => false;

        public float Quantity => 3f;

        public Vec3d Pos => Position;

        public Vec3f ParentVelocity => null!;

        public int LightEmission => 0;

        public EvolvingNatFloat OpacityEvolve => default!;

        public EvolvingNatFloat RedEvolve => default!;

        public EvolvingNatFloat GreenEvolve => default!;

        public EvolvingNatFloat BlueEvolve => default!;

        public EnumParticleModel ParticleModel => EnumParticleModel.Quad;

        public float Size => 1f;

        public EvolvingNatFloat SizeEvolve => default!;

        public EvolvingNatFloat[] VelocityEvolve => null!;

        public float GravityEffect => 0;

        public float LifeLength => 1f;

        public int VertexFlags => 0;

        public bool SelfPropelled => false;

        public bool TerrainCollision => false;

        public float SecondarySpawnInterval => 0;

        public IParticlePropertiesProvider[] SecondaryParticles => null!;

        public IParticlePropertiesProvider[] DeathParticles => null!;

        public bool RandomVelocityChange => false;

        public void Init(ICoreAPI api)
        {
        }

        public void BeginParticle()
        {
        }

        public Vec3f GetVelocity(Vec3d pos) => new(0, 1, 0);

        public int GetRgbaColor(ICoreClientAPI capi) => 0;

        public void ToBytes(BinaryWriter writer) => Position.ToBytes(writer);

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver) => Position = Vec3d.CreateFromBytes(reader);

        public void PrepareForSecondarySpawn(ParticleBase particleInstance)
        {
        }
    }
}
