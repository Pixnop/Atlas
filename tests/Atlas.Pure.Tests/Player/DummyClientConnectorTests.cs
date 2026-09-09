using System.Net;
using Atlas.Internal.Player;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Player;

/// <summary>The three facts <see cref="DummyClientConnector"/> owns for the whole player seam and
/// that need no live server to check: the handshake packet order, the derivation of a joined
/// client's dummy UDP endpoint, which <c>KickedPlayerCleanup</c> reconstructs from the client id
/// alone, and the slot claim over the engine's socket array.</summary>
public class DummyClientConnectorTests
{
    [Fact]
    public void HandshakePackets_Should_QueryTheLoginTokenBeforeIdentifying_When_Built()
    {
        // Stratum's first-packet gate (2026-08-24) only creates the ConnectedClient when the
        // first Data message is a ServerQuery (15) or a LoginTokenQuery (33); an
        // identification-first join is dropped with nothing logged. The order IS the fix.
        Packet_Client[] handshake = DummyClientConnector.HandshakePackets("bob");

        Assert.Equal(2, handshake.Length);
        Assert.Equal(33, handshake[0].Id);
        Assert.NotNull(handshake[0].LoginTokenQuery);
        Assert.Equal(1, handshake[1].Id);
        Assert.Equal("bob", handshake[1].Identification.Playername);
    }

    [Fact]
    public void UdpEndpointOf_Should_UseTheClientIdAsThePort_When_Derived()
    {
        IPEndPoint endpoint = DummyClientConnector.UdpEndpointOf(7);

        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.Equal(7, endpoint.Port);
    }

    [Fact]
    public void UdpEndpointOf_Should_RebuildTheSameKey_When_CalledTwiceForOneClient()
    {
        // KickedPlayerCleanup looks the registration up, and restores it, through this
        // derivation alone; the dictionary is keyed by endpoint, so value equality is the
        // contract, not reference identity.
        Assert.Equal(DummyClientConnector.UdpEndpointOf(3), DummyClientConnector.UdpEndpointOf(3));
        Assert.NotEqual(DummyClientConnector.UdpEndpointOf(3), DummyClientConnector.UdpEndpointOf(4));
    }

    [Fact]
    public void ClaimTcpSlot_Should_FillTheFirstHoleAndSkipTheEngineSlot_When_OneIsFree()
    {
        // Slot 1 is the engine's own TCP listener slot: free or not, Atlas never takes it, so a
        // free slot 2 is the claim even though 1 comes first.
        var socket = new DummyTcpNetServer();
        NetServer?[] sockets = [new DummyTcpNetServer(), null, null];

        (NetServer?[] installed, int slot) = DummyClientConnector.ClaimTcpSlot(sockets, socket);

        Assert.Equal(2, slot);
        Assert.Same(sockets, installed); // filled in place: the parse loop re-reads this array
        Assert.Same(socket, sockets[2]);
        Assert.Null(sockets[1]);
    }

    [Fact]
    public void ClaimTcpSlot_Should_GrowByOne_When_EveryUsableSlotIsTaken()
    {
        var taken = new DummyTcpNetServer();
        var socket = new DummyTcpNetServer();
        NetServer?[] sockets = [taken, null];

        (NetServer?[] installed, int slot) = DummyClientConnector.ClaimTcpSlot(sockets, socket);

        Assert.Equal(2, slot);
        Assert.NotSame(sockets, installed);
        Assert.Equal(3, installed.Length);
        Assert.Same(taken, installed[0]);
        Assert.Null(installed[1]);
        Assert.Same(socket, installed[2]);
    }
}
