using System.Net;
using NSubstitute;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>The opt-in client listener's engine checks: the shape check passes on the engine the
/// suite compiled against and names what is missing when it does not, and the dummy-connection
/// reader tells a test player's connection from a real one. The same shape check runs against
/// every listed install in <see cref="EngineContractTests"/>.</summary>
public class EngineCompatClientListenerTests
{
    [Fact]
    public void ValidateClientListener_Should_Pass_When_TheLoadedEngineHasEveryMember()
        => EngineCompat.ValidateClientListener();

    [Fact]
    public void CheckClientListenerShape_Should_PassOnTheCompiledAgainstEngine_When_GivenItsServerType()
        => EngineCompat.CheckClientListenerShape(typeof(ServerMain), EngineCompat.ShortGameVersion);

    [Fact]
    public void CheckClientListenerShape_Should_NameTheMissingTypeAndTheVersion_When_TheEngineLacksIt()
    {
        // A server type from an assembly that holds none of the engine's network types: what a
        // version or fork that dropped them looks like to the check.
        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.CheckClientListenerShape(typeof(FakeServer), "9.9.9"));

        Assert.Contains("Vintagestory.Common.NetServer", ex.Message);
        Assert.Contains("9.9.9", ex.Message);
        Assert.Contains("loopback listener", ex.Message);
    }

    [Fact]
    public void IsDummyConnection_Should_BeTrue_When_TheSocketIsADummyConnection()
    {
        var client = new ConnectedClient(1) { Socket = new DummyNetConnection() };

        Assert.True(EngineCompat.IsDummyConnection(client));
    }

    [Fact]
    public void IsDummyConnection_Should_BeFalse_When_TheSocketIsARealConnection()
    {
        NetConnection socket = Substitute.For<NetConnection>();
        socket.RemoteEndPoint().Returns(new IPEndPoint(IPAddress.Loopback, 40000));
        var client = new ConnectedClient(2) { Socket = socket };

        Assert.False(EngineCompat.IsDummyConnection(client));
    }

    private sealed class FakeServer
    {
    }
}
