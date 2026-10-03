using System.Diagnostics;

namespace Atlas.ClientSpike;

/// <summary>Step A: the embedded host opens a loopback listener a real client can reach, and the
/// dummy test players keep working next to it.</summary>
public class ListenerTests(ITestOutputHelper output)
{
    private static ServerHost NewHost(bool listener) => new(
        new WorldOptions(), [], SpikePaths.OwnOutputDirectory) { OpenClientListener = listener };

    internal static string[] SocketLinesFor(int port)
    {
        using Process ss = Process.Start(new ProcessStartInfo("ss", "-ltnuH")
        {
            RedirectStandardOutput = true,
        })!;
        string text = ss.StandardOutput.ReadToEnd();
        ss.WaitForExit();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains($":{port} ") || l.EndsWith($":{port}"))
            .ToArray();
    }

    [Fact]
    public async Task Listener_Should_BindLoopbackOnly_AndKeepDummyPlayersWorking()
    {
        await using ServerHost host = NewHost(listener: true);
        await host.StartAsync();

        ClientEndpoint endpoint = Assert.IsType<ClientEndpoint>(host.ClientEndpoint);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.True(endpoint.Port > 1024);
        Assert.Equal(32, endpoint.Password.Length);

        string[] lines = SocketLinesFor(endpoint.Port);
        foreach (string line in lines)
        {
            output.WriteLine("ss: " + line);
        }

        // One TCP listener and one UDP socket, both on 127.0.0.1 and nowhere else.
        Assert.Equal(2, lines.Length);
        Assert.Single(lines, l => l.StartsWith("tcp") && l.Contains("LISTEN") && l.Contains($"127.0.0.1:{endpoint.Port} "));
        Assert.Single(lines, l => l.StartsWith("udp") && l.Contains($"127.0.0.1:{endpoint.Port} "));
        Assert.DoesNotContain(lines, l => l.Contains($"0.0.0.0:{endpoint.Port}") || l.Contains($"*:{endpoint.Port}") || l.Contains($"[::]:{endpoint.Port}"));

        // The dummy players still work: two of them, joined and in the world.
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer a = await world.JoinPlayer("SpikeDummyA");
            ITestPlayer b = await world.JoinPlayer("SpikeDummyB");
            Assert.NotNull(a.Entity);
            Assert.NotNull(b.Entity);
            Assert.NotEqual(a.Entity.EntityId, b.Entity.EntityId);
            await a.Say("hello from a dummy while the listener is open");
        });

        // The engine's own view of the config the listener armed.
        await host.RunOnGameThreadAsync((api, ticks) =>
        {
            var server = (Vintagestory.Server.ServerMain)typeof(Vintagestory.Server.ServerMain).Assembly
                .GetType("Vintagestory.Server.ServerCoreAPI")!.GetField("server", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(api)!;
            Assert.False(server.Config.VerifyPlayerAuth);
            Assert.Equal(endpoint.Password, server.Config.Password);
            Assert.IsType<Vintagestory.Server.TcpNetServer>(server.MainSockets[1]);
            Assert.IsType<Vintagestory.Server.Network.UdpNetServer>(server.UdpSockets[1]);
            Assert.NotNull(server.MainSockets[0]);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Listener_Should_StayClosed_ByDefault()
    {
        await using ServerHost host = NewHost(listener: false);
        await host.StartAsync();
        Assert.Null(host.ClientEndpoint);
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer a = await world.JoinPlayer("SpikeDummyC");
            Assert.NotNull(a.Entity);
        });
    }

    [Fact]
    public async Task Listener_Should_ReleaseThePort_WhenTheHostIsDisposed()
    {
        int port;
        await using (ServerHost host = NewHost(listener: true))
        {
            await host.StartAsync();
            port = host.ClientEndpoint!.Port;
            Assert.NotEmpty(SocketLinesFor(port));
        }

        // Teardown joins the game thread and disposes the engine, which closes the sockets.
        string[] after = SocketLinesFor(port);
        foreach (string line in after)
        {
            output.WriteLine("after dispose: " + line);
        }

        Assert.Empty(after);
    }
}
