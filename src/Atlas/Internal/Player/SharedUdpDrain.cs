using Atlas.Internal.Bootstrap;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Player;

/// <summary>Empties, on every server pass, the client receive buffer of the one dummy UDP server
/// every test player of a host shares, so what the server sends them over UDP does not pile up
/// for the whole scenario.</summary>
/// <remarks><para>Why it is safe to drop: <c>DummyUdpNetServer.SendToClient</c> enqueues each
/// outbound UDP packet (entity positions, mostly) into that buffer, and the only code that ever
/// dequeues it is the real client's <c>DummyUdpNetClient</c>, which an embedded test host does not
/// have. Nothing in Atlas reads it either: UDP mod channels are not observed
/// (<see cref="Api.IClientObservations"/>). Measured with one player and 100 hens nearby, 1 UDP
/// packet per pass and about 6 KB retained per pass, about 13 MB per minute.</para>
/// <para>One per host, not one per player: <c>UdpSockets[0]</c> is a single instance for the whole
/// server (<see cref="DummyClientConnector.Connect"/>), so a listener per player would clear the
/// same queue several times a pass. It is registered once at boot and lives in the server's own
/// event manager, so it goes with the server when the host is disposed, and there is nothing to
/// unregister. Before the first player joins, <c>UdpSockets[0]</c> is empty and the pass costs a
/// null check. The clearing takes the engine's lock for the queue, so a send from another thread
/// either lands before it or after it, never inside it
/// (<see cref="EngineCompat.ClearUdpClientBuffer"/>).</para></remarks>
internal sealed class SharedUdpDrain
{
    private readonly Func<UNetServer?> _udpSocket;
    private bool _reported;

    private SharedUdpDrain(Func<UNetServer?> udpSocket) => _udpSocket = udpSocket;

    /// <summary>Registers the host's one drain on the server's per-pass tick event.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="udpSocket">Reads the server's <c>UdpSockets[0]</c>, empty until the first test
    /// player joins. A delegate so a pure test can drive it.</param>
    /// <remarks>Runs on the game thread, once per host, after the server API is handed over.</remarks>
    public static void Install(ICoreServerAPI api, Func<UNetServer?> udpSocket)
    {
        var drain = new SharedUdpDrain(udpSocket);

        // The overload with an error handler, never the two-argument one: without a handler an
        // exception escaping the listener aborts the rest of the pass's listeners.
        api.Event.RegisterGameTickListener(drain.OnPass, drain.OnPassError, 1);
    }

    /// <summary>The per-pass listener: empties the shared UDP buffer once a test player created
    /// it. Internal for the engine test that calls it directly.</summary>
    /// <param name="deltaTime">The engine's elapsed time since the last call, unused.</param>
    internal void OnPass(float deltaTime)
    {
        if (_udpSocket() is DummyUdpNetServer udp)
        {
            EngineCompat.ClearUdpClientBuffer(udp);
        }
    }

    /// <summary>The listener's error handler: says so once on stderr, nothing else can report it
    /// (the drain has no reader), and keeps the pass going.</summary>
    /// <param name="error">What the listener threw.</param>
    private void OnPassError(Exception error)
    {
        if (!_reported)
        {
            _reported = true;
            Console.Error.WriteLine($"[Atlas] emptying the test players' shared UDP queue failed, it will keep growing: {error}");
        }
    }
}
