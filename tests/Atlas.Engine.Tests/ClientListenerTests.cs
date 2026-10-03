using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>The opt-in loopback listener a real game client connects to: closed unless asked
/// for, loopback only when open, released with the host, untouched by the test players that ride
/// the same engine, and a password the engine enforces. No game client runs here (see
/// <see cref="CharacterGateTests"/> for the bare protocol client that stands in for one).</summary>
/// <remarks>Each listener fact boots its own host: the listener is armed at boot, and a host
/// that must be disposed inside the test to observe its port cannot be shared.</remarks>
[Trait("Category", "E2E")]
public class ClientListenerTests
{
    [Fact]
    public async Task Listener_Should_StayClosed_When_NotAskedFor()
    {
        // A host built the way every other test and every scenario class builds one: the
        // property is never touched, so this is the default.
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();

        Assert.Null(host.ClientEndpoint);
        await host.RunScenarioAsync(async world =>
        {
            ListenerProbes.ListenerView before = ListenerProbes.View(world.Api);
            Assert.Null(before.TcpSlot);
            Assert.Null(before.UdpSlot);

            // Nothing was armed: the engine keeps its own defaults, authentication on and no password.
            Assert.True(before.VerifyPlayerAuth);
            Assert.True(string.IsNullOrEmpty(before.Password));

            ITestPlayer player = await world.JoinPlayer("NoListenerDummy");
            Assert.NotNull(player.Entity);
            ListenerProbes.ListenerView after = ListenerProbes.View(world.Api);
            Assert.Null(after.TcpSlot);
            Assert.Null(after.UdpSlot);
        });
    }

    [Fact]
    public async Task Listener_Should_BindLoopbackOnlyAndLeaveTestPlayersAlone_When_AskedFor()
    {
        await using ServerHost host = NewHost(listener: true);
        await host.StartAsync();

        ClientEndpoint endpoint = Assert.IsType<ClientEndpoint>(host.ClientEndpoint);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.InRange(endpoint.Port, 1024, 65535);
        Assert.Matches("^[0-9A-F]{32}$", endpoint.Password);

        // The same view `ss -ltnuH` gives: one TCP listener and one UDP socket on this port, and
        // both on 127.0.0.1, not on the wildcard address and not on IPv6.
        IPEndPoint tcp = Assert.Single(TcpListenersOn(endpoint.Port));
        IPEndPoint udp = Assert.Single(UdpListenersOn(endpoint.Port));
        Assert.Equal(IPAddress.Loopback, tcp.Address);
        Assert.Equal(IPAddress.Loopback, udp.Address);

        // It accepts on the loopback address, and on no other address of this machine.
        Assert.True(await CanConnect(IPAddress.Loopback, endpoint.Port), "the listener did not accept on 127.0.0.1");
        foreach (IPAddress other in NonLoopbackAddresses())
        {
            Assert.False(await CanConnect(other, endpoint.Port), $"the listener accepted on {other}, which is not loopback");
        }

        await host.RunScenarioAsync(async world =>
        {
            ListenerProbes.ListenerView view = ListenerProbes.View(world.Api);
            Assert.False(view.VerifyPlayerAuth);
            Assert.Equal(endpoint.Password, view.Password);
            Assert.Equal("TcpNetServer", view.TcpSlot);
            Assert.Equal("UdpNetServer", view.UdpSlot);

            // Test players join and talk exactly as before: they ride their own slots, are never
            // asked for the password, and the real listener's slot is still the listener's.
            ITestPlayer first = await world.JoinPlayer("ListenerDummyA");
            ITestPlayer second = await world.JoinPlayer("ListenerDummyB");
            Assert.NotEqual(first.Entity.EntityId, second.Entity.EntityId);
            var heard = new List<string>();
            world.Api.Event.PlayerChat += (IServerPlayer player, int channel, ref string message, ref string data, Vintagestory.API.Datastructures.BoolRef consumed) => heard.Add(message);
            await first.Say("hello while the listener is open");
            Assert.Contains(heard, line => line.Contains("hello while the listener is open", StringComparison.Ordinal));

            ListenerProbes.ListenerView after = ListenerProbes.View(world.Api);
            Assert.Equal("TcpNetServer", after.TcpSlot);
            Assert.Equal("UdpNetServer", after.UdpSlot);
            Assert.Equal("DummyTcpNetServer", after.TcpDummySlot);
            Assert.Equal(endpoint.Password, after.Password);
        });
    }

    [Fact]
    public async Task Listener_Should_ReleaseItsPort_When_TheHostIsDisposed()
    {
        int port = 0;
        ServerHost host = NewHost(listener: true);
        try
        {
            await host.StartAsync();
            port = host.ClientEndpoint!.Port;
            Assert.Single(TcpListenersOn(port));
            Assert.Single(UdpListenersOn(port));
        }
        finally
        {
            await host.DisposeAsync();
        }

        Assert.Empty(TcpListenersOn(port));
        Assert.Empty(UdpListenersOn(port));

        // Both protocols can bind it again, so nothing is left half closed.
        var tcp = new TcpListener(IPAddress.Loopback, port);
        tcp.Start();
        tcp.Stop();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task Open_Should_RetryOnTheNextPortAndLeaveNothingOpen_When_TheFirstCandidatesAreTaken()
    {
        await using ServerHost host = NewHost(listener: false);
        await host.StartAsync();

        // One candidate taken for TCP (the engine's TCP start fails), one taken for UDP only (its
        // TCP start succeeds and has to be undone when the UDP start fails), then a free one.
        var heldTcp = new TcpListener(IPAddress.Loopback, 0);
        heldTcp.Start();
        int tcpTaken = ((IPEndPoint)heldTcp.LocalEndpoint).Port;
        var heldUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int udpTaken = ((IPEndPoint)heldUdp.Client.LocalEndPoint!).Port;
        int free = ClientListener.FindFreePort();
        var candidates = new Queue<int>([tcpTaken, udpTaken, free]);

        ClientEndpoint? endpoint = null;
        await host.RunOnGameThreadAsync((api, _) =>
        {
            endpoint = ListenerProbes.Open(api, candidates.Dequeue);
            return Task.CompletedTask;
        });

        Assert.Equal(free, endpoint!.Port);
        Assert.Empty(candidates);
        Assert.Single(TcpListenersOn(free));
        Assert.Single(UdpListenersOn(free));

        // The failed attempts released what they had started: with the outside holders gone,
        // both ports are free for both protocols (a leaked TCP listener would still hold
        // udpTaken).
        heldTcp.Stop();
        heldUdp.Dispose();
        foreach (int port in new[] { tcpTaken, udpTaken })
        {
            var tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }

        // A second opening is refused: Atlas does not stack a listener on one it opened.
        AtlasSetupException refused = await Assert.ThrowsAsync<AtlasSetupException>(
            () => host.RunOnGameThreadAsync((api, _) =>
            {
                ListenerProbes.Open(api, () => throw new InvalidOperationException("must not look for a port"));
                return Task.CompletedTask;
            }));
        Assert.Contains("already in use", refused.Message);
        Assert.Single(TcpListenersOn(free));
    }

    [Fact]
    public async Task StartAsync_Should_FailWithASetupErrorAndTearTheEngineDown_When_NoPortCanBeBound()
    {
        var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        int taken = ((IPEndPoint)held.LocalEndpoint).Port;
        string dataPath;
        try
        {
            {
                await using ServerHost host = NewHost(listener: true);
                host.ClientListenerPortSource = () => taken;

                AtlasSetupException failure = await Assert.ThrowsAsync<AtlasSetupException>(host.StartAsync);

                Assert.Contains("could not bind a port", failure.Message);
                Assert.Null(host.ClientEndpoint);
                dataPath = host.DataPath;
            }

            // The launched engine was stopped and disposed like on any other boot failure: the only
            // thing still on that port is the holder this test opened.
            Assert.Single(TcpListenersOn(taken));
            Assert.Empty(UdpListenersOn(taken));

            // And the process is fit for the next host, which a leaked engine (its statics, its
            // threads) would have broken.
            await using ServerHost next = NewHost(listener: true);
            await next.StartAsync();
            Assert.NotNull(next.ClientEndpoint);
        }
        finally
        {
            held.Stop();
        }

        // A crashed host keeps its scratch as post-mortem evidence; this one is not needed.
        Directory.Delete(dataPath, recursive: true);
    }

    [Fact]
    public async Task Listener_Should_RefuseAConnection_When_ItPresentsTheWrongPassword()
    {
        await using ServerHost host = NewHost(listener: true);
        await host.StartAsync();
        ClientEndpoint endpoint = host.ClientEndpoint!;

        await host.RunScenarioAsync(async world =>
        {
            using LoopbackClient wrong = LoopbackClient.Connect(endpoint);
            wrong.Identify("WrongPassword", "not-the-password");
            await world.Until(() => wrong.ServerClosed);
            Assert.DoesNotContain(world.Api.World.AllOnlinePlayers, p => p.PlayerName == "WrongPassword");

            // The control: the same handshake with the right password gets a player.
            using LoopbackClient right = LoopbackClient.Connect(endpoint);
            right.Identify("RightPassword", endpoint.Password);
            await world.Until(() => world.Api.World.AllOnlinePlayers.Any(p => p.PlayerName == "RightPassword" && p.Entity != null));
            Assert.False(right.ServerClosed);
        });
    }

    private static ServerHost NewHost(bool listener)
        => new(new WorldOptions(), [], TestPaths.OwnOutputDirectory) { OpenClientListener = listener };

    private static IPEndPoint[] TcpListenersOn(int port)
        => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port).ToArray();

    private static IPEndPoint[] UdpListenersOn(int port)
        => IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(e => e.Port == port).ToArray();

    private static IEnumerable<IPAddress> NonLoopbackAddresses()
        => NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));

    private static async Task<bool> CanConnect(IPAddress address, int port)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
