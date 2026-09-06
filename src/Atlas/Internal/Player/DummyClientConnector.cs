using System.Net;
using Atlas.Internal.Bootstrap;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Player;

/// <summary>Joins a headless player into a running <see cref="ServerMain"/> over an in-memory
/// dummy socket pair, the same mechanism the game's own singleplayer client uses to talk to its
/// local server.</summary>
/// <remarks>Proven live by the issue #4 headless-player spike: a single <see cref="Packet_Client"/> with
/// <c>Id = 1</c> (identification) is enough to make the server spawn a real, world-present
/// <see cref="Vintagestory.API.Common.EntityPlayer"/>, because <c>IsSinglePlayerClient</c>
/// (set automatically once the connection rides a <see cref="DummyNetConnection"/>) bypasses
/// <c>VerifyPlayerWithAuthServer</c> entirely - the same auth-skip real singleplayer relies on.
/// Packet 11 (<c>RequestJoin</c>) is sent once the entity has spawned: it is what wires up the
/// player's <c>InventoryManager</c> (confirmed by inspecting <c>HandleRequestJoin</c>, which
/// calls into every registered server system's <c>OnPlayerJoin</c>) - a deviation from the
/// spike, which only sent packet 1 and did not exercise <c>GiveItem</c>. Packets 26
/// (<c>ClientLoaded</c>)/29 (<c>PlayerReady</c>) complete the join (see
/// <see cref="SendClientLoadedAndReady"/>): the server itself transitions the client to
/// <c>EnumClientState.Playing</c>, making test players visible to everything that filters on
/// <c>ConnectedClient.IsPlayingClient</c> or counts Playing players (issue #74; e.g. Stratum's
/// distance-based throttling, <c>NearestPlayer</c>, playing-count broadcasts). They were
/// originally skipped as out of scope for a headless player - a scope decision, not a technical
/// constraint, and the 1.20.12/1.21.7/1.22.3 handlers confirmed no constraint exists (no
/// preconditions beyond a joined player; every post-transition engine path is either
/// dummy-socket-safe or guarded by <c>IsSinglePlayerClient</c>, which dummy connections
/// are).</remarks>
internal static class DummyClientConnector
{
    /// <summary>The <c>MainSockets</c> index the engine reserves for its real TCP listener
    /// (assigned when a dedicated server, or a singleplayer host opened to LAN, starts
    /// listening). Never claimed for a test player, so neither engine path can collide with
    /// Atlas even though a headless host exercises neither today.</summary>
    private const int EngineTcpSlot = 1;

    /// <summary>Claims a free <c>MainSockets</c> slot on <paramref name="server"/> with a dummy
    /// TCP socket (growing the array when every slot is taken), shares the single
    /// <c>UdpSockets[0]</c> dummy UDP server across players, then sends an identification packet
    /// for <paramref name="playerName"/>.</summary>
    /// <param name="server">The live server. <see cref="Internal.Hosting.ServerHost"/> boots with
    /// <c>isDedicatedServer: false</c> and never populates any socket slot, so slot 0 is free for
    /// the first headless player; later players get their own slot past the engine-reserved
    /// index. One dummy TCP socket carries exactly one connection (each
    /// <c>DummyTcpNetServer</c> owns a single <c>DummyNetConnection</c> and attributes every
    /// inbound message to it), which is why concurrent players mean one socket per player rather
    /// than one shared socket: the server's <c>PacketParsingLoop</c> iterates whatever
    /// <c>MainSockets</c> array is installed, so growing it is all the multiplexing needed.</param>
    /// <param name="playerName">The player name to identify as.</param>
    /// <returns>The dummy connection, so a caller can send follow-up packets (e.g.
    /// <see cref="RequestJoin"/>) and register the UDP endpoint once the entity has spawned.</returns>
    /// <exception cref="Api.AtlasSetupException">Thrown when <c>UdpSockets[0]</c> is occupied by
    /// something other than the shared dummy UDP server - the engine hard-casts that exact slot
    /// for every singleplayer-type client, so it cannot be repurposed.</exception>
    /// <remarks>Runs on the game thread. Does not wait for the server to process the packet;
    /// callers must pump ticks afterwards for <c>HandlePlayerIdentification</c> to run and the
    /// entity to spawn.</remarks>
    public static DummyPlayerConnection Connect(ServerMain server, string playerName)
    {
        var tcpNetwork = new DummyNetwork();
        tcpNetwork.Start();
        var dummyTcpServer = new DummyTcpNetServer();
        dummyTcpServer.SetNetwork(tcpNetwork);
        int tcpSlot = ClaimTcpSlot(server, dummyTcpServer);

        // CRITICAL (spike finding): ServerMain.SendServerIdentification() unconditionally hard-casts
        // UdpSockets[0] to DummyUdpNetServer for any IsSinglePlayerClient connection once
        // serverAssetsSentLocally is true. Skipping this crashes the game thread with an
        // InvalidCastException the moment the "client" reaches that point. That same hard-cast is
        // also why the UDP side is a singleton shared by every test player: whichever instance
        // sits in UdpSockets[0] is the one the engine wires every singleplayer-type client to.
        DummyUdpNetServer dummyUdpServer;
        switch (server.UdpSockets[0])
        {
            case null:
                var udpNetwork = new DummyNetwork();
                udpNetwork.Start();
                dummyUdpServer = new DummyUdpNetServer();
                dummyUdpServer.SetNetwork(udpNetwork);
                server.UdpSockets[0] = dummyUdpServer;
                break;
            case DummyUdpNetServer shared:
                dummyUdpServer = shared;
                break;
            default:
                server.MainSockets[tcpSlot] = null;
                throw new Api.AtlasSetupException(
                    $"UdpSockets[0] is occupied by a {server.UdpSockets[0].GetType().Name}, not " +
                    "the dummy UDP server Atlas shares between test players. The engine " +
                    "hard-casts that exact slot for every singleplayer-type client, so Atlas " +
                    "cannot join a test player on this server.");
        }

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

                // Read from the loaded engine's metadata, never the GameVersion consts: consts
                // are baked into Atlas's IL at compile time, and the server hard-rejects a
                // network-version mismatch in HandlePlayerIdentification, so a prebuilt Atlas
                // on an older engine would have every join kicked (see EngineCompat).
                NetworkVersion = EngineCompat.NetworkVersion,
                ShortGameVersion = EngineCompat.ShortGameVersion,
            },
        };

        // DummyTcpNetServer.ReadMessage() synthesizes the NetworkMessageType.Connect event itself,
        // the first time its receive buffer has any queued packet - queuing packet 1 doubles as
        // connecting; no separate connect step exists or is needed.
        dummyClient.Send(Serialize(identification));
        return new DummyPlayerConnection(dummyClient, dummyTcpServer, dummyUdpServer, tcpSlot);
    }

    /// <summary>Releases the TCP slot <paramref name="connection"/> claimed, so a failed join
    /// (or a kicked player's completed removal) does not leave it permanently claimed for the
    /// rest of the class host's lifetime.</summary>
    /// <param name="server">The live server whose slot should be freed.</param>
    /// <param name="connection">The connection whose TCP slot to release.</param>
    /// <remarks>Runs on the game thread. Only the player's own TCP slot is released, and only
    /// while it still holds this player's own socket - a stale release (e.g. the second
    /// <see cref="KickedPlayerCleanup"/> pass after a rejoin already reclaimed the slot) must not
    /// detach a later player's socket. The shared UDP server at <c>UdpSockets[0]</c> stays
    /// installed, because other joined players are wired to that exact instance (see
    /// <see cref="Connect"/>) and it holds no per-player claim worth reclaiming. Necessary
    /// because the server can disconnect a rejected client (e.g. an invalid player name, or the
    /// version-drift symptom <see cref="Api.AtlasSetupException"/> in
    /// <c>WorldSession.WaitForJoin</c> diagnoses) well after <see cref="Connect"/> already
    /// claimed the slot - the rejection happens at the identification-packet level, one step past
    /// the socket claim, so the claim itself is never rolled back by the engine.</remarks>
    public static void ReleaseSlot(ServerMain server, DummyPlayerConnection connection)
    {
        if (server.MainSockets[connection.TcpSlot] == connection.TcpServer)
        {
            server.MainSockets[connection.TcpSlot] = null;
        }
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

    /// <summary>Sends packets 26 (<c>ClientLoaded</c>) and 29 (<c>PlayerReady</c>) over an
    /// already-joined dummy connection, completing the engine's own join sequence: the server's
    /// handlers announce the join and transition the client to <c>EnumClientState.Playing</c>.</summary>
    /// <param name="connection">The dummy connection returned by <see cref="Connect"/>.</param>
    /// <remarks>Runs on the game thread. Mirrors the real client exactly: it sends both packets
    /// bodyless and back-to-back from its own-player-data handler (verified by decompile,
    /// <c>GeneralPacketHandler.HandlePlayerData</c> on 1.22.3), and no server-side handler reads
    /// a packet body on 1.20.12/1.21.7/1.22.3 (26 is <c>HandleClientLoaded</c> everywhere; 29 is
    /// <c>HandlePlayerReady</c> on 1.22, <c>HandleClientPlaying</c> before - same packet id, same
    /// transition). Sending the real packets rather than poking <c>ConnectedClient.State</c> lets
    /// each engine version run its own transition: 26 fires <c>PlayerNowPlaying</c>, broadcasts
    /// the join message and stamps <c>MillisecsAtConnect</c>; 29 sets <c>Playing</c>, syncs land
    /// claims and (1.22+) stamps <c>LastActivityTotalMs</c> and fires <c>PlayerReady</c>. Must
    /// only be sent once the join is complete (entity spawned, inventories wired): the handlers
    /// dereference <c>client.Player</c> immediately. Safe to send unconditionally even when a
    /// mod kicked the player mid-join: the engine's dispatch drops packets from a removed client
    /// at its own <c>client.Player.client == client</c> guard.</remarks>
    public static void SendClientLoadedAndReady(DummyPlayerConnection connection)
    {
        connection.TcpClient.Send(Serialize(new Packet_Client { Id = 26 })); // PacketHandlers[26] = HandleClientLoaded
        connection.TcpClient.Send(Serialize(new Packet_Client { Id = 29 })); // PacketHandlers[29] = HandlePlayerReady
    }

    /// <summary>Sends packet 4 (<c>ChatLine</c>) over an already-joined dummy connection, exactly
    /// the packet <c>ClientPackets.Chat</c> builds when a real client's chat box sends a
    /// line.</summary>
    /// <param name="connection">The dummy connection returned by <see cref="Connect"/>.</param>
    /// <param name="message">The chat line text, unmodified. A leading <c>/</c> is what makes
    /// the server's own <c>HandleChatMessage</c> dispatch it as a command
    /// (<c>api.commandapi.Execute</c>) instead of a broadcast chat line - same message, same
    /// packet, the server alone decides which.</param>
    /// <remarks>Runs on the game thread. Sent on <c>GlobalConstants.GeneralChatGroup</c>, the
    /// group a real client's chat box defaults to unless the player switched tabs
    /// (<c>HudDialogChat.game.currentGroupid</c>, verified by decompile). Does not wait for the
    /// server to process the packet; see <see cref="TestPlayer.Say"/> for the tick wait callers
    /// get for free. Verified by decompile, byte-identical <c>Packet_Client</c>/<c>Packet_ChatLine</c>
    /// shape and dispatch id (<c>PacketHandlers[4] = HandleChatLine</c>) on 1.21.7, 1.22.3 and
    /// 1.22.7.</remarks>
    public static void Say(DummyPlayerConnection connection, string message)
    {
        var chat = new Packet_Client
        {
            Id = 4, // PacketHandlers[4] = HandleChatLine
            Chatline = new Packet_ChatLine
            {
                Message = message,
                Groupid = GlobalConstants.GeneralChatGroup,
            },
        };

        connection.TcpClient.Send(Serialize(chat));
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
    /// it outright keeps shutdown logs clean. The endpoint's port is the client ID: the endpoint
    /// itself is never used for traffic (no real UDP ever flows), but the UDP server's endpoint
    /// registry is a dictionary keyed by endpoint, shared by every test player, so each
    /// registration needs a distinct fake address.</remarks>
    public static void RegisterUdpEndpoint(DummyPlayerConnection connection, int clientId)
        => connection.UdpServer.Add(UdpEndpointOf(clientId), clientId);

    /// <summary>Rebuilds the dummy UDP endpoint <see cref="RegisterUdpEndpoint"/> registers for a
    /// joined client: the port IS the client id, which is what makes the registration
    /// reconstructible from the id alone (see <see cref="KickedPlayerCleanup"/>, which reads its
    /// absence as "the disconnect teardown has started" and restores it before re-running the
    /// teardown).</summary>
    /// <param name="clientId">The server-assigned client ID for the joined player.</param>
    /// <returns>The endpoint this client is (or was) registered under.</returns>
    public static IPEndPoint UdpEndpointOf(int clientId) => new(IPAddress.Loopback, clientId);

    /// <summary>Tells whether <paramref name="client"/> is still the registered client for its id
    /// on <paramref name="server"/>: false once a disconnect removed it, or a rejoin under the
    /// same name replaced it. The identity check is the point - the id alone comes back for the
    /// next client.</summary>
    /// <param name="server">The live server.</param>
    /// <param name="client">The client to look up.</param>
    /// <returns>Whether the client is still registered.</returns>
    public static bool IsRegistered(ServerMain server, ConnectedClient client)
        => server.Clients.TryGetValue(client.Id, out ConnectedClient? registered)
            && ReferenceEquals(registered, client);

    /// <summary>The slot-claiming rule itself, over the socket array rather than the server:
    /// take the first free slot that is not the engine's reserved one, and when there is none,
    /// hand back a copy grown by one with the socket in its new last slot.</summary>
    /// <param name="sockets">The server's current socket array; a filled hole is written into
    /// it in place, which is what the engine's re-reading parse loop expects.</param>
    /// <param name="socket">The socket to install.</param>
    /// <returns>The array to install on the server (the same instance when a hole was filled,
    /// the grown copy otherwise) and the claimed index.</returns>
    internal static (NetServer?[] Sockets, int Slot) ClaimTcpSlot(NetServer?[] sockets, NetServer socket)
    {
        for (int i = 0; i < sockets.Length; i++)
        {
            if (i != EngineTcpSlot && sockets[i] == null)
            {
                sockets[i] = socket;
                return (sockets, i);
            }
        }

        var grown = new NetServer?[sockets.Length + 1];
        Array.Copy(sockets, grown, sockets.Length);
        grown[sockets.Length] = socket;
        return (grown, sockets.Length);
    }

    /// <summary>Installs <paramref name="socket"/> into the first free <c>MainSockets</c> slot
    /// of <paramref name="server"/>, growing the array by one when every usable slot is
    /// taken.</summary>
    /// <param name="server">The live server to claim a slot on.</param>
    /// <param name="socket">The dummy TCP socket to install.</param>
    /// <returns>The claimed index.</returns>
    /// <remarks>The engine's <c>PacketParsingLoop</c> re-reads the <c>MainSockets</c> property
    /// every pass and skips null entries, and <c>MainSockets</c> is a settable property, so both
    /// filling a hole (left by <see cref="ReleaseSlot"/>) and swapping in a grown copy are safe
    /// here on the game thread, between pump passes.</remarks>
    private static int ClaimTcpSlot(ServerMain server, DummyTcpNetServer socket)
    {
        (NetServer?[] sockets, int slot) = ClaimTcpSlot(server.MainSockets, socket);
        server.MainSockets = sockets!;
        return slot;
    }

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
