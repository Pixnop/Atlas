using System.Net;
using Atlas.Internal.Hosting;
using NSubstitute;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The decision of the character gate over real engine client records: a real connection
/// is marked as having created its character, a test player's dummy connection is not touched.
/// Whether the survival mod then skips its dialog, and that this handler runs before the survival
/// mod's own, is covered against a booted server by <c>CharacterGateTests</c> in the engine
/// suite.</summary>
public class CharacterGateTests
{
    private const int DummyClientId = 7;
    private const int RealClientId = 8;

    [Fact]
    public void LetPast_Should_SetTheCreateCharacterFlag_When_TheConnectionIsReal()
    {
        (CachingConcurrentDictionary<int, ConnectedClient> clients, IServerPlayer real, _) = Table();

        bool letPast = CharacterGate.LetPast(clients, real);

        Assert.True(letPast);

        // Compared by content: the arrays are different instances, and what matters is that the
        // survival mod's own Deserialize<bool>(GetModdata(key)) reads true back out of it.
        real.Received(1).SetModdata("createCharacter", Arg.Is<byte[]>(data => SerializerUtil.Deserialize<bool>(data, false)));
    }

    [Fact]
    public void LetPast_Should_LeaveATestPlayerUntouched_When_TheConnectionIsADummy()
    {
        (CachingConcurrentDictionary<int, ConnectedClient> clients, _, IServerPlayer dummy) = Table();

        bool letPast = CharacterGate.LetPast(clients, dummy);

        Assert.False(letPast);
        dummy.DidNotReceiveWithAnyArgs().SetModdata(default!, default!);
    }

    [Fact]
    public void LetPast_Should_DoNothing_When_TheServerNoLongerKnowsThePlayer()
    {
        (CachingConcurrentDictionary<int, ConnectedClient> clients, _, _) = Table();
        IServerPlayer gone = Substitute.For<IServerPlayer>();
        gone.ClientId.Returns(99);

        Assert.False(CharacterGate.LetPast(clients, gone));

        gone.DidNotReceiveWithAnyArgs().SetModdata(default!, default!);
    }

    [Fact]
    public void ModDataKey_Should_BeTheKeyTheSurvivalModReads_When_Read()
        => Assert.Equal("createCharacter", CharacterGate.ModDataKey);

    [Fact]
    public void SerializedTrue_Should_RoundTripThroughTheEnginesDeserializer_When_Set()
    {
        // What the survival mod does with the stored bytes: Deserialize<bool>(GetModdata(key)).
        Assert.True(SerializerUtil.Deserialize<bool>(SerializerUtil.Serialize(true), false));
    }

    private static (CachingConcurrentDictionary<int, ConnectedClient> Clients, IServerPlayer Real, IServerPlayer Dummy) Table()
    {
        var clients = new CachingConcurrentDictionary<int, ConnectedClient>
        {
            [DummyClientId] = new ConnectedClient(DummyClientId) { Socket = new DummyNetConnection() },
            [RealClientId] = new ConnectedClient(RealClientId) { Socket = RealSocket() },
        };
        return (clients, PlayerOn(RealClientId), PlayerOn(DummyClientId));
    }

    private static NetConnection RealSocket()
    {
        NetConnection socket = Substitute.For<NetConnection>();
        socket.RemoteEndPoint().Returns(new IPEndPoint(IPAddress.Loopback, 40000));
        return socket;
    }

    private static IServerPlayer PlayerOn(int clientId)
    {
        IServerPlayer player = Substitute.For<IServerPlayer>();
        player.ClientId.Returns(clientId);
        return player;
    }
}
