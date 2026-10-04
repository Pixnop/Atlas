using System.Net;
using System.Net.Sockets;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Contract of the loopback port draw shared by the data-file tokens: a port both
/// protocols can bind, a port handed out earlier is never handed out again, and a draw that
/// cannot find one fails with a named setup error instead of looping.</summary>
public class FreePortTests
{
    [Fact]
    public void Find_Should_ReturnAPortBothProtocolsCanBind_When_Asked()
    {
        for (int i = 0; i < 20; i++)
        {
            int port = FreePort.Find();

            Assert.InRange(port, 1024, 65535);
            var tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }
    }

    [Fact]
    public void Find_Should_ConvertToAParameterlessDelegate_When_PassedAsAMethodGroup()
    {
        Func<int> draw = FreePort.Find;

        Assert.InRange(draw(), 1024, 65535);
    }

    [Fact]
    public void Find_Should_SkipAPortAlreadyTaken_When_TheCandidateRepeatsIt()
    {
        int first = FreePort.Find();
        int second = FreePort.Find([first]);
        var candidates = new Queue<int>([first, first, second]);

        int found = FreePort.Find([first], candidates.Dequeue);

        Assert.Equal(second, found);
        Assert.Empty(candidates);
    }

    [Fact]
    public void Find_Should_DrawAgain_When_TheCandidateIsBoundForUdp()
    {
        using var held = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int heldPort = ((IPEndPoint)held.Client.LocalEndPoint!).Port;
        int free = FreePort.Find([heldPort]);
        var candidates = new Queue<int>([heldPort, free]);

        int found = FreePort.Find(taken: null, candidates.Dequeue);

        Assert.Equal(free, found);
    }

    [Fact]
    public void Find_Should_ThrowAtlasSetupException_When_EveryCandidateIsTaken()
    {
        using var held = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int heldPort = ((IPEndPoint)held.Client.LocalEndPoint!).Port;
        int draws = 0;

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => FreePort.Find(
            taken: null,
            () =>
            {
                draws++;
                return heldPort;
            }));

        Assert.Equal(FreePort.MaxAttempts, draws);
        Assert.Contains(FreePort.MaxAttempts.ToString(), ex.Message);
    }
}
