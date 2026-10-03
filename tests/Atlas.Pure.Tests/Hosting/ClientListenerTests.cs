using System.Net;
using System.Net.Sockets;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The parts of the loopback client listener that need no engine: the endpoint's shape,
/// the password, the port choice and the retry loop. What the engine does with the sockets is
/// covered by <c>ClientListenerTests</c> in the engine suite.</summary>
public class ClientListenerTests
{
    [Fact]
    public void Loopback_Should_BeTheIpv4LoopbackAddress_When_Read()
        => Assert.Equal(IPAddress.Loopback, IPAddress.Parse(ClientListener.Loopback));

    [Fact]
    public void Address_Should_JoinHostAndPort_When_Formatted()
        => Assert.Equal("127.0.0.1:41234", new ClientEndpoint("127.0.0.1", 41234, "pw").Address);

    [Fact]
    public void NewPassword_Should_Be32HexDigits_When_Generated()
    {
        string password = ClientListener.NewPassword();

        Assert.Equal(32, password.Length);
        Assert.All(password, c => Assert.True(Uri.IsHexDigit(c), $"'{c}' is not a hex digit"));
    }

    [Fact]
    public void NewPassword_Should_DifferEachTime_When_GeneratedRepeatedly()
        => Assert.Equal(50, Enumerable.Range(0, 50).Select(_ => ClientListener.NewPassword()).Distinct().Count());

    [Fact]
    public void FindFreePort_Should_ReturnAPortBothProtocolsCanBind_When_Asked()
    {
        for (int i = 0; i < 20; i++)
        {
            int port = ClientListener.FindFreePort();

            Assert.InRange(port, 1024, 65535);
            var tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }
    }

    [Fact]
    public void Bind_Should_ReturnTheFirstSuccess_When_TheFirstCandidateBinds()
    {
        var offered = new List<int>();
        int ports = 100;

        int bound = ClientListener.Bind(
            () => ports++,
            port =>
            {
                offered.Add(port);
                return (int?)port;
            });

        Assert.Equal(100, bound);
        Assert.Equal([100], offered);
    }

    [Fact]
    public void Bind_Should_TryTheNextCandidate_When_TheFirstIsTaken()
    {
        var offered = new List<int>();
        int ports = 100;

        int bound = ClientListener.Bind(
            () => ports++,
            port =>
            {
                offered.Add(port);
                return port >= 102 ? port : (int?)null;
            });

        Assert.Equal(102, bound);
        Assert.Equal([100, 101, 102], offered);
    }

    [Fact]
    public void Bind_Should_GiveUpWithASetupError_When_EveryCandidateIsTaken()
    {
        int attempts = 0;

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ClientListener.Bind(
                () => 5,
                _ =>
                {
                    attempts++;
                    return (int?)null;
                }));

        Assert.Equal(ClientListener.MaxPortAttempts, attempts);
        Assert.Contains($"{ClientListener.MaxPortAttempts} attempts", ex.Message);
    }
}
