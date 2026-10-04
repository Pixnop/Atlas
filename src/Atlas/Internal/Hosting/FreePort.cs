using System.Net;
using System.Net.Sockets;
using Atlas.Api;

namespace Atlas.Internal.Hosting;

/// <summary>Draws a loopback port that is free for TCP and UDP at once, for the places Atlas
/// hands a port to something that binds it a moment later (the <c>{{atlas:port:NAME}}</c>
/// token in seeded data files).</summary>
/// <remarks>The port comes from the system's ephemeral range and is released before it is
/// returned, so a race with another process is still possible between the draw and the later
/// bind; the window is short (the host's boot) and, unlike a port frozen in a file, it does not
/// repeat on every run.</remarks>
internal static class FreePort
{
    /// <summary>How many candidates <see cref="Find(ICollection{int})"/> tries before it gives up.</summary>
    internal const int MaxAttempts = 20;

    /// <summary>Picks a port that is free for TCP and for UDP at once, by asking the system for
    /// an ephemeral TCP port and probing the same number for UDP.</summary>
    /// <param name="taken">Ports already handed out, which are never returned again, or
    /// <see langword="null"/>. A candidate that is in it counts as one of the
    /// <see cref="MaxAttempts"/> attempts.</param>
    /// <returns>A loopback port that was free for both protocols a moment ago.</returns>
    /// <exception cref="AtlasSetupException">Thrown when every candidate was taken, which would
    /// take a pathological machine.</exception>
    internal static int Find(ICollection<int>? taken) => Find(taken, EphemeralTcpPort);

    /// <summary>The same as <see cref="Find(ICollection{int})"/> with nothing handed out yet. A
    /// real overload rather than a default argument, so the method group converts to a
    /// <see cref="Func{TResult}"/> for a caller that only needs one port.</summary>
    /// <returns>A loopback port that was free for both protocols a moment ago.</returns>
    /// <exception cref="AtlasSetupException">Thrown when every candidate was taken, which would
    /// take a pathological machine.</exception>
    internal static int Find() => Find(null, EphemeralTcpPort);

    /// <summary>The same as <see cref="Find(ICollection{int})"/>, with the source of candidate
    /// ports handed in: a test names ports that are taken, to drive the redraw.</summary>
    /// <param name="taken">Ports already handed out, or <see langword="null"/>.</param>
    /// <param name="candidate">Gives the next candidate port.</param>
    /// <returns>A loopback port that was free for both protocols a moment ago.</returns>
    /// <exception cref="AtlasSetupException">Thrown after <see cref="MaxAttempts"/> candidates
    /// that were taken.</exception>
    internal static int Find(ICollection<int>? taken, Func<int> candidate)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            int port = candidate();
            if (taken?.Contains(port) == true)
            {
                continue;
            }

            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return port;
            }
            catch (SocketException)
            {
                // The ephemeral TCP port is in use for UDP: draw another.
            }
        }

        throw new AtlasSetupException(
            $"No loopback port was free for TCP and UDP at once after {MaxAttempts} draws.");
    }

    private static int EphemeralTcpPort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }
}
