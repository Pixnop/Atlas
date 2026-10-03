using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Hosting;

/// <summary>Opens, on an embedded (non dedicated) server that already finished <c>Launch()</c>,
/// the two listeners the engine's dedicated server opens for real clients, bound to the loopback
/// address only.</summary>
/// <remarks><para>What the engine does for a dedicated server (1.22.3 decompile,
/// <c>ServerMain.AfterConfigLoaded</c> and <c>ServerMain.startSockets</c>): put a
/// <see cref="TcpNetServer"/> in <c>MainSockets[1]</c> and a <see cref="UdpNetServer"/> in
/// <c>UdpSockets[1]</c>, give both the same ip and port, and start them. The same recipe is what
/// the game's own <c>/allowlan</c> command runs on a singleplayer server
/// (<c>CmdToggleAllowLan</c>), so it is the engine's own way to open a non dedicated server to
/// real connections. Slot 1 is the one <see cref="Player.DummyClientConnector"/> never claims, so
/// test players and a real client coexist: dummy players ride <c>MainSockets[0]</c> and
/// <c>UdpSockets[0]</c> (and the grown slots past 1), the real client rides slot 1 of both.</para>
/// <para>Binding to <see cref="Loopback"/> and not to the engine's default (every address) is the
/// first of two guards, the random password is the second; <c>VerifyPlayerAuth</c> is switched
/// off because a real client is going to present a token the embedded host never asked an
/// authentication server about.</para></remarks>
internal static class ClientListener
{
    /// <summary>The only address the listeners bind to.</summary>
    internal const string Loopback = "127.0.0.1";

    private const int MaxPortAttempts = 20;

    /// <summary>Opens the listeners on a free loopback port and arms the host's config for a real
    /// client: authentication check off, random server password.</summary>
    /// <param name="server">The launched embedded server.</param>
    /// <returns>Where a client connects.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no free port was found.</exception>
    /// <remarks>Runs on the game thread, between <c>Launch()</c> and the first pump pass.</remarks>
    internal static ClientEndpoint Open(ServerMain server)
    {
        server.Config.VerifyPlayerAuth = false;
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        server.Config.Password = password;

        for (int attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            int port = FindFreePort();
            if (TryStart(server, port))
            {
                return new ClientEndpoint(Loopback, port, password);
            }
        }

        throw new InvalidOperationException(
            $"No free loopback port for the client listener after {MaxPortAttempts} attempts.");
    }

    /// <summary>Picks a port that is free for TCP and for UDP at once, by asking the system for
    /// an ephemeral TCP port and probing the same number for UDP. A race with another process is
    /// still possible between the probe and the bind, which <see cref="Open"/> covers by
    /// retrying.</summary>
    /// <returns>A port that was free for both protocols a moment ago.</returns>
    private static int FindFreePort()
    {
        while (true)
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return port;
            }
            catch (SocketException)
            {
                // The ephemeral TCP port is taken for UDP: ask again.
            }
        }
    }

    private static bool TryStart(ServerMain server, int port)
    {
        var tcp = new TcpNetServer();
        tcp.SetIpAndPort(Loopback, port);
        var udp = new UdpNetServer(server.Clients);
        udp.SetIpAndPort(Loopback, port);
        try
        {
            tcp.Start();
            udp.Start();
        }
        catch (SocketException)
        {
            tcp.Dispose();
            udp.Dispose();
            return false;
        }

        server.MainSockets[1] = tcp;
        server.UdpSockets[1] = udp;
        return true;
    }
}
