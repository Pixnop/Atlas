using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Internal.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Engine.Tests.Support;

/// <summary>The few engine internals the client-observation scenarios touch directly. They are
/// kept out of the test methods on purpose: every member is <see cref="MethodImplOptions.NoInlining"/>
/// and only called once a host has booted, because a method that names a <c>VintagestoryLib</c>
/// type is resolved when it is compiled, and that assembly only becomes loadable through the
/// host's own late resolver (see the reference in the project file).</summary>
internal static class EngineProbes
{
    /// <summary>Counts the live entries of the engine's game-tick listener list, where every
    /// <c>RegisterGameTickListener</c> of the server API ends up.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The number of registered listeners.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TickListeners(ICoreServerAPI api)
    {
        EventManager manager = ((ServerMain)api.World).EventManager;
        FieldInfo field = typeof(EventManager).GetField("GameTickListenersEntity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((System.Collections.IEnumerable)field.GetValue(manager)!).Cast<object?>().Count(listener => listener != null);
    }

    /// <summary>Sends the player a particle packet naming a provider class that is registered
    /// nowhere, which the engine accepts and a client (or the drain) cannot decode.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="player">The player to send to.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SendUndecodableParticles(ICoreServerAPI api, IServerPlayer player)
        => ((ServerMain)api.World).SendPacket(
            player,
            new Packet_Server
            {
                Id = 61,
                SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "atlas-no-such-provider", Data = [] },
            });

    /// <summary>Sends, from the player's own connection, the packet a real client sends when it
    /// closes (<c>Leave</c>, packet 14).</summary>
    /// <param name="player">The joined test player.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SendClientLeave(ITestPlayer player)
    {
        var connection = (DummyPlayerConnection)typeof(TestPlayer)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(player)!;
        var leave = new Packet_Client { Id = 14, Leave = new Packet_ClientLeave { Reason = 0 } };
        connection.TcpClient.Send(Packet_ClientSerializer.SerializeToBytes(leave));
    }

    /// <summary>Counts the packets waiting in the shared dummy UDP connection's client receive
    /// buffer (<c>UdpSockets[0]</c>): what the server sent every test player over UDP and nothing
    /// has taken out yet.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The queue's length, or 0 when no test player ever joined (no UDP socket yet).</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UdpClientBufferCount(ICoreServerAPI api)
    {
        if (((ServerMain)api.World).UdpSockets[0] is not { } udp)
        {
            return 0;
        }

        return ClientBufferCount(NonPublicField(udp.GetType(), "network").GetValue(udp)!);
    }

    /// <summary>Deserializes, with the engine's own serializer, every message a test player's
    /// observations hold parked and undecoded: an oracle for what the drop-at-dequeue filter let
    /// through, independent of the filter's own id reading.</summary>
    /// <param name="player">The joined test player.</param>
    /// <returns>The parked packets, oldest first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Packet_Server[] ParkedPackets(ITestPlayer player)
    {
        object parked = NonPublicField(typeof(ClientObservations), "_parked").GetValue(player.Client)!;
        return
        [
            .. ((System.Collections.IEnumerable)parked).Cast<object>().Select(entry =>
            {
                var message = (NetIncomingMessage)entry.GetType().GetProperty("Message")!.GetValue(entry)!;
                return Packet_ServerSerializer.DeserializeBuffer(message.message, message.messageLength, new Packet_Server());
            }),
        ];
    }

    /// <summary>Adds up the length of every message a test player's observations hold parked and
    /// undecoded, read off the messages themselves: an oracle for
    /// <see cref="IClientObservations.UnreadBytes"/> that shares none of its bookkeeping.</summary>
    /// <param name="player">The joined test player.</param>
    /// <returns>The parked messages' total length in bytes.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long ParkedByteCount(ITestPlayer player)
    {
        object parked = NonPublicField(typeof(ClientObservations), "_parked").GetValue(player.Client)!;
        return ((System.Collections.IEnumerable)parked).Cast<object>().Sum(
            entry => (long)((NetIncomingMessage)entry.GetType().GetProperty("Message")!.GetValue(entry)!).messageLength);
    }

    /// <summary>Hides an entity from one client the way a fork that filters what a client may see
    /// does it in the middle of a session: forgets the entity in the client's tracked set and
    /// sends it the despawn packet (reason <see cref="EnumDespawnReason.Unload"/>) the engine's own
    /// tracking pass would send when the entity left its range. The vanilla tracking pass has no
    /// filter, so it finds the entity in range again a few passes later and sends it back, which
    /// stands for the fork lifting the hide.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="observer">The player the entity is hidden from.</param>
    /// <param name="entity">The entity to hide.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void HideEntityFrom(ICoreServerAPI api, ITestPlayer observer, Entity entity)
    {
        var server = (ServerMain)api.World;
        ConnectedClient client = server.Clients[((IServerPlayer)observer.Player).ClientId];
        client.TrackedEntities.Remove(entity.EntityId);
        server.SendPacket(
            client.Id,
            ServerPackets.GetEntityDespawnPacket(
            [
                new EntityDespawn { EntityId = entity.EntityId, DespawnData = new EntityDespawnData { Reason = EnumDespawnReason.Unload } },
            ]));
    }

    /// <summary>Counts the live game-tick listeners that are Atlas's shared UDP drain.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The number of registered drains: one per host.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UdpDrainListeners(ICoreServerAPI api)
    {
        EventManager manager = ((ServerMain)api.World).EventManager;
        FieldInfo field = typeof(EventManager).GetField("GameTickListenersEntity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((IEnumerable<GameTickListener?>)field.GetValue(manager)!).Count(listener => listener?.Handler.Target is SharedUdpDrain);
    }

    /// <summary>Tells whether the engine's off-thread half of a save is still in flight: the
    /// flag <c>/autosavenow</c> raises and the chunk thread lowers once the savegame blob, the
    /// dirty chunks and the map chunks are written.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>Whether an off-thread save is running.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool OffThreadSaveRunning(ICoreServerAPI api)
        => ((ChunkServerThread)Atlas.Internal.Bootstrap.EngineCompat.ChunkThreadField.GetValue((ServerMain)api.World)!).runOffThreadSaveNow;

    /// <summary>Reads one SaveGame mod-data entry straight out of the savegame blob in the
    /// database, bypassing the live in-memory <c>SaveGame</c>: what is there is what a save
    /// wrote, whatever the world holds in memory.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="key">The mod-data key.</param>
    /// <returns>The persisted bytes, or <see langword="null"/> when the database has no such entry.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static byte[]? PersistedSaveGameData(ICoreServerAPI api, string key)
    {
        var chunkThread = (ChunkServerThread)Atlas.Internal.Bootstrap.EngineCompat.ChunkThreadField.GetValue((ServerMain)api.World)!;
        var database = (GameDatabase)Atlas.Internal.Bootstrap.EngineCompat.GameDatabaseField.GetValue(chunkThread)!;
        return database.GetSaveGame().ModData.GetValueOrDefault(key);
    }

    /// <summary>Creates the engine's role record for a player before it ever joins, on the given
    /// role: the "pre-created player data" a test might expect to survive the join.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="uid">The uid the joining test player will present.</param>
    /// <param name="name">The name it will present.</param>
    /// <param name="roleCode">The role the record is created on.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PrecreateRoleRecord(ICoreServerAPI api, string uid, string name, string roleCode)
        => ((ServerMain)api.World).PlayerDataManager.GetOrCreateServerPlayerData(uid, name).RoleCode = roleCode;

    /// <summary>Counts the handlers subscribed to the engine's <c>PlayerJoin</c> event for mods,
    /// the one <c>ICoreServerAPI.Event.PlayerJoin</c> adds to.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The number of subscribed handlers.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PlayerJoinHandlers(ICoreServerAPI api)
        => ((PlayerDelegate?)NonPublicField(typeof(ServerEventManager), "OnPlayerJoin").GetValue(((ServerMain)api.World).ModEventManager))
            ?.GetInvocationList().Length ?? 0;

    /// <summary>Finds the entity the engine created for a connecting player, which exists from
    /// the moment it identifies itself, before the join request.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="name">The player's name.</param>
    /// <returns>The entity, or <see langword="null"/> when no client of that name has one.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static EntityPlayer? EntityOfConnecting(ICoreServerAPI api, string name)
        => ((ServerMain)api.World).Clients.Values.FirstOrDefault(client => client.PlayerName == name)?.Entityplayer;

    private static int ClientBufferCount(object network)
        => ((Queue<object>)NonPublicField(typeof(DummyNetwork), "ClientReceiveBuffer").GetValue(network)!).Count;

    private static FieldInfo NonPublicField(Type type, string name)
        => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
