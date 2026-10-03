using System.Net;
using Atlas.Internal.Hosting;
using NSubstitute;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The decision of the character gate over real engine client records: a real connection
/// is marked as having created its character, a test player's dummy connection is not touched.
/// Whether the survival mod then skips its dialog, that this handler runs before the survival
/// mod's own, and that the flag is written on the player, is covered against a booted server by
/// <c>CharacterGateTests</c> in the engine suite.</summary>
/// <remarks>The decision is tested apart from the player: on 1.22.7 <c>IPlayer</c> has an internal
/// member, which a proxy generator cannot implement, so no <c>IServerPlayer</c> can be substituted
/// there.</remarks>
public class CharacterGateTests
{
    private const int DummyClientId = 7;
    private const int RealClientId = 8;

    [Fact]
    public void AppliesTo_Should_BeTrue_When_TheConnectionIsReal()
        => Assert.True(CharacterGate.AppliesTo(Table(), RealClientId));

    [Fact]
    public void AppliesTo_Should_BeFalse_When_TheConnectionIsADummy()
        => Assert.False(CharacterGate.AppliesTo(Table(), DummyClientId));

    [Fact]
    public void AppliesTo_Should_BeFalse_When_TheServerNoLongerKnowsTheClient()
        => Assert.False(CharacterGate.AppliesTo(Table(), 99));

    [Fact]
    public void ModDataKey_Should_BeTheKeyTheSurvivalModReads_When_Read()
        => Assert.Equal("createCharacter", CharacterGate.ModDataKey);

    [Fact]
    public void SerializedTrue_Should_RoundTripThroughTheEnginesDeserializer_When_Set()
    {
        // What the survival mod does with the stored bytes: Deserialize<bool>(GetModdata(key)).
        Assert.True(SerializerUtil.Deserialize<bool>(SerializerUtil.Serialize(true), false));
    }

    private static CachingConcurrentDictionary<int, ConnectedClient> Table()
        => new()
        {
            [DummyClientId] = new ConnectedClient(DummyClientId) { Socket = new DummyNetConnection() },
            [RealClientId] = new ConnectedClient(RealClientId) { Socket = RealSocket() },
        };

    private static NetConnection RealSocket()
    {
        NetConnection socket = Substitute.For<NetConnection>();
        socket.RemoteEndPoint().Returns(new IPEndPoint(IPAddress.Loopback, 40000));
        return socket;
    }
}
