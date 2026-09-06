using System.Net;
using Atlas.Internal.Player;

namespace Atlas.Pure.Tests.Player;

/// <summary>The two facts <see cref="DummyClientConnector"/> owns for the whole player seam and
/// that need no live server to check: the derivation of a joined client's dummy UDP endpoint,
/// which <c>KickedPlayerCleanup</c> reconstructs from the client id alone, and the slot claim
/// over the engine's socket array.</summary>
public class DummyClientConnectorTests
{
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
}
