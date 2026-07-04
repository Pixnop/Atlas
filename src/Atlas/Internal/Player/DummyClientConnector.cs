using System.Net;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Player;

/// <summary>Joins a headless player into a running <see cref="ServerMain"/> over an in-memory
/// dummy socket pair, the same mechanism the game's own singleplayer client uses to talk to its
/// local server.</summary>
/// <remarks>Proven live by the issue #4 feasibility spike (see
/// <c>.superpowers/sdd/player-spike-report.md</c>): a single <see cref="Packet_Client"/> with
/// <c>Id = 1</c> (identification) is enough to make the server spawn a real, world-present
/// <see cref="Vintagestory.API.Common.EntityPlayer"/>, because <c>IsSinglePlayerClient</c>
/// (set automatically once the connection rides a <see cref="DummyNetConnection"/>) bypasses
/// <c>VerifyPlayerWithAuthServer</c> entirely - the same auth-skip real singleplayer relies on.
/// Packet 11 (<c>RequestJoin</c>) is sent once the entity has spawned: it is what wires up the
/// player's <c>InventoryManager</c> (confirmed by inspecting <c>HandleRequestJoin</c>, which
/// calls into every registered server system's <c>OnPlayerJoin</c>) - a deviation from the
/// spike, which only sent packet 1 and did not exercise <c>GiveItem</c>. Packets 26
/// (<c>ClientLoaded</c>)/29 (<c>PlayerReady</c>) are still deliberately not sent: they exist to
/// reach the <c>Playing</c> client state (visibility to systems like <c>NearestPlayer</c>),
/// which is out of scope for a headless test player.</remarks>
internal static class DummyClientConnector
{
    /// <summary>Claims socket slot 0 on <paramref name="server"/> with a dummy TCP/UDP pair, then
    /// sends a single identification packet for <paramref name="playerName"/>.</summary>
    /// <param name="server">The live server. Must not already occupy <c>MainSockets[0]</c>/
    /// <c>UdpSockets[0]</c>: <see cref="Internal.Hosting.ServerHost"/> boots with
    /// <c>isDedicatedServer: false</c> but never populates either slot, so slot 0 is free for the
    /// first (and only) headless player. A second call throws instead of silently colliding with
    /// the mechanism a real singleplayer client would otherwise use.</param>
    /// <param name="playerName">The player name to identify as.</param>
    /// <returns>The dummy connection, so a caller can send follow-up packets (e.g.
    /// <see cref="RequestJoin"/>) and register the UDP endpoint once the entity has spawned.</returns>
    /// <exception cref="Api.AtlasSetupException">Thrown when slot 0 is already occupied - see
    /// the single-occupancy note above.</exception>
    /// <remarks>Runs on the game thread. Does not wait for the server to process the packet;
    /// callers must pump ticks afterwards for <c>HandlePlayerIdentification</c> to run and the
    /// entity to spawn.</remarks>
    public static DummyPlayerConnection Connect(ServerMain server, string playerName)
    {
        if (server.MainSockets[0] != null || server.UdpSockets[0] != null)
        {
            throw new Api.AtlasSetupException(
                "A headless test player is already joined to this world. Atlas claims socket " +
                "slot 0 for its single dummy-network test player and that slot is fixed-size and " +
                "single-occupancy in the embedded server (the same slot a real singleplayer " +
                "client would use); concurrent multiple test players needs a multiplexing shim " +
                "over that slot and is tracked as follow-up work, not supported yet.");
        }

        var tcpNetwork = new DummyNetwork();
        tcpNetwork.Start();
        var dummyTcpServer = new DummyTcpNetServer();
        dummyTcpServer.SetNetwork(tcpNetwork);
        server.MainSockets[0] = dummyTcpServer;

        // CRITICAL (spike finding): ServerMain.SendServerIdentification() unconditionally hard-casts
        // UdpSockets[0] to DummyUdpNetServer for any IsSinglePlayerClient connection once
        // serverAssetsSentLocally is true. Skipping this crashes the game thread with an
        // InvalidCastException the moment the "client" reaches that point.
        var udpNetwork = new DummyNetwork();
        udpNetwork.Start();
        var dummyUdpServer = new DummyUdpNetServer();
        dummyUdpServer.SetNetwork(udpNetwork);
        server.UdpSockets[0] = dummyUdpServer;

        var dummyClient = new DummyTcpNetClient();
        dummyClient.SetNetwork(tcpNetwork);

        var identification = new Packet_Client
        {
            Id = 1, // PacketHandlers[1] = HandlePlayerIdentification
            Identification = new Packet_ClientIdentification
            {
                Playername = playerName,
                MdProtocolVersion = "1.0",
                MpToken = null,
                ServerPassword = null,
                PlayerUID = $"atlas-{playerName}",
                ViewDistance = 128,
                RenderMetaBlocks = 0,
                NetworkVersion = GameVersion.NetworkVersion,
                ShortGameVersion = GameVersion.ShortGameVersion,
            },
        };

        // DummyTcpNetServer.ReadMessage() synthesizes the NetworkMessageType.Connect event itself,
        // the first time its receive buffer has any queued packet - queuing packet 1 doubles as
        // connecting; no separate connect step exists or is needed.
        dummyClient.Send(Serialize(identification));
        return new DummyPlayerConnection(dummyClient, dummyUdpServer);
    }

    /// <summary>Sends packet 11 (<c>RequestJoin</c>) over an already-identified dummy connection.</summary>
    /// <param name="connection">The dummy connection returned by <see cref="Connect"/>.</param>
    /// <param name="languageCode">The client's language code.</param>
    /// <remarks>Runs on the game thread. Must only be sent once the identified client's entity
    /// has spawned (<c>HandleRequestJoin</c> reads <c>ConnectedClient.Entityplayer</c>
    /// immediately); callers must pump ticks after <see cref="Connect"/> and before calling this
    /// method. This is what populates <c>IServerPlayer.InventoryManager.Inventories</c> - without
    /// it, the inventory manager has zero inventories and any hotbar/backpack access throws.</remarks>
    public static void RequestJoin(DummyPlayerConnection connection, string languageCode = "en")
    {
        var requestJoin = new Packet_Client
        {
            Id = 11, // PacketHandlers[11] = HandleRequestJoin
            RequestJoin = new Packet_ClientRequestJoin { Language = languageCode },
        };

        connection.TcpClient.Send(Serialize(requestJoin));
    }

    /// <summary>Registers the joined client's UDP endpoint, so a later disconnect (e.g. embedded
    /// server shutdown) can clean it up without throwing.</summary>
    /// <param name="connection">The dummy connection returned by <see cref="Connect"/>.</param>
    /// <param name="clientId">The server-assigned client ID for the joined player.</param>
    /// <remarks>Runs on the game thread. Spike finding: without this, no real UDP traffic ever
    /// populates <c>DummyUdpNetServer.EndPoints</c>, and <c>DummyUdpNetServer.Remove(player)</c>
    /// (called from <c>ServerMain.DisconnectPlayer</c> during shutdown) throws
    /// <see cref="KeyNotFoundException"/> looking up a client ID that was never added. The
    /// exception was already caught and logged one level up (best-effort shutdown), but avoiding
    /// it outright keeps shutdown logs clean.</remarks>
    public static void RegisterUdpEndpoint(DummyPlayerConnection connection, int clientId)
        => connection.UdpServer.Add(new IPEndPoint(IPAddress.Loopback, 0), clientId);

    /// <summary>Serializes a <see cref="Packet_Client"/> to its exact wire length.</summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <returns>The serialized bytes, sliced to the stream's written length.</returns>
    /// <remarks><see cref="CitoMemoryStream.ToArray"/> returns the internal growable buffer
    /// (starts at 16 bytes, doubles on overflow), not a length-exact copy - the result must be
    /// sliced to <see cref="CitoMemoryStream.Position"/> or the dummy socket ships trailing
    /// garbage as part of the message (spike finding).</remarks>
    private static byte[] Serialize(Packet_Client packet)
    {
        var stream = new CitoMemoryStream();
        packet.SerializeTo(stream);
        int length = stream.Position();
        byte[] exact = new byte[length];
        Array.Copy(stream.ToArray(), exact, length);
        return exact;
    }
}
