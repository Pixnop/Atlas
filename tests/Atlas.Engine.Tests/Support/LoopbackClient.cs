using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Atlas.Internal.Player;

namespace Atlas.Engine.Tests.Support;

/// <summary>A bare protocol client over a real TCP socket to the opt-in client listener: it sends
/// the packets a game client sends to join and reads and drops everything the server answers. It
/// is not the game client (nothing renders, nothing answers the server's requests), only enough
/// of one to give the server a connection that is not a dummy one, with the engine's own wire
/// format. Packets go through the same builders the test players use, so the two cannot drift
/// apart.</summary>
/// <remarks>Members that name engine packet types are <see cref="MethodImplOptions.NoInlining"/>
/// and only called once a host has booted, like <see cref="EngineProbes"/>.</remarks>
internal sealed class LoopbackClient : IDisposable
{
    private readonly TcpClient _tcp = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _drain;
    private volatile bool _serverClosed;
    private int _disposed;

    /// <summary>Gets a value indicating whether the server closed the connection (a refused
    /// password, a kick, the end of the host).</summary>
    public bool ServerClosed => _serverClosed;

    /// <summary>Connects to the listener and starts dropping what the server sends.</summary>
    /// <param name="endpoint">Where the host's listener is.</param>
    /// <returns>The connected client.</returns>
    public static LoopbackClient Connect(ClientEndpoint endpoint)
    {
        var client = new LoopbackClient();
        client._tcp.Connect(endpoint.Host, endpoint.Port);
        client._drain = Task.Run(client.DrainAsync);
        return client;
    }

    /// <summary>Sends the identification.</summary>
    /// <param name="name">The player name.</param>
    /// <param name="password">The server password to present.</param>
    /// <remarks>Only the identification, and first: a test player's handshake starts with the
    /// login token query, which the engine's real socket drops when a new connection opens with
    /// it ("invalid packet received": the first byte of a new connection's first packet must be
    /// the key of the identification or of the ping). The token is only for UDP, which this
    /// client does not use.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Identify(string name, string password)
    {
        Packet_Client identification = DummyClientConnector.HandshakePackets(name)[1];
        identification.Identification.ServerPassword = password;
        Send(identification);
    }

    /// <summary>Sends a ping reply (packet 2): with the identification, the one packet the engine
    /// accepts as the first on a new connection and from a client that has not identified yet.</summary>
    /// <remarks>The server registers a connection when its first packet arrives, so this puts the
    /// connection on the server's client table ahead of the identification, which a test can then
    /// send as late as it wants. The login token query cannot do it: the engine's socket drops a
    /// connection that opens with it (see <see cref="Identify"/>).</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SendPingReply()
        => Send(new Packet_Client { Id = 2 });

    /// <summary>Sends the join request (packet 11), once the server has spawned the entity.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RequestJoin()
        => Send(new Packet_Client { Id = 11, RequestJoin = new Packet_ClientRequestJoin { Language = "en" } });

    /// <summary>Sends packets 26 and 29, what a game client sends once it has loaded the level
    /// and its character dialog, if any, is out of the way.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SendClientLoadedAndReady()
    {
        Send(new Packet_Client { Id = 26 });
        Send(new Packet_Client { Id = 29 });
    }

    /// <summary>Closes the connection. Safe to call twice, so a test can end a client early and
    /// still keep its <c>using</c>.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        _tcp.Dispose();
        try
        {
            _drain?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The drain ends on the socket being closed under it; nothing to report.
        }

        _stop.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Send(Packet_Client packet)
    {
        byte[] payload = DummyClientConnector.Serialize(packet);

        // The engine's framing: a four byte big endian length (top bit: compressed), then the packet.
        byte[] framed = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(framed, payload.Length);
        payload.CopyTo(framed, 4);
        _tcp.GetStream().Write(framed);
    }

    private async Task DrainAsync()
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            NetworkStream stream = _tcp.GetStream();
            while (await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // A reset or a close ends the read the same way a clean end of stream does.
        }

        _serverClosed = true;
    }
}
