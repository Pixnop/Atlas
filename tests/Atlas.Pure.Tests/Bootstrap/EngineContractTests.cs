using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Atlas.Internal.Hosting;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>Per-version contract net for every engine shape Atlas resolves by reflection: loads
/// one real install's two engine assemblies into a collectible <see cref="AssemblyLoadContext"/>
/// and runs the resolvers against the loaded types, with no server boot. One row per install
/// (see <see cref="CompatInstallsAttribute"/>), about a second each.</summary>
/// <remarks><para>This is the cheap half of the compatibility promise: it catches SHAPE drift
/// (the issue #49 class - a renamed field, a member that turned from a field into a property, a
/// Stop overload that changed signature) on every supported version at pure-suite speed, from a
/// single Atlas binary. It can never catch a BEHAVIOURAL change, so it complements the E2E
/// matrix rather than replacing any leg of it.</para>
/// <para>Two rules the probe that designed this test had to learn: the engine types must come
/// from the install's own load context, never from <c>typeof</c> (which would resolve to the
/// compile-time install and make every row assert the same shape twice); and that includes the
/// <c>fieldType</c> argument of <see cref="EngineCompat.ResolveNonPublicInstanceField"/> for
/// engine-owned types, while framework-owned ones (<see cref="int"/>,
/// <see cref="Dictionary{TKey, TValue}"/>, <see cref="Queue{T}"/>) are shared with the default
/// context and can stay as <c>typeof</c>.</para></remarks>
public class EngineContractTests
{
    /// <summary>What the resolver's own fail-fast message would say; the contract net only needs
    /// the resolution to succeed, so one placeholder serves every call.</summary>
    private const string Consequence = "checked by the per-version engine contract test.";

    /// <summary>The exit-state field names <see cref="EngineCompat.ResolveExitStateField"/> may
    /// resolve to, across every supported version.</summary>
    private static readonly string[] ExitStateFieldNames = ["exitState", "exit"];

    [Theory]
    [CompatInstalls]
    public void Resolvers_Should_BindEveryAdaptedShape_When_RunAgainstARealInstall(string install)
    {
        // A listed install that holds no engine assemblies fails here rather than vanishing from
        // the matrix: whoever set the variable asked for this version to be checked.
        Assert.True(
            CompatInstallsAttribute.HoldsEngineAssemblies(install),
            $"{CompatInstallsAttribute.ListVariable} lists '{install}', which holds no engine dlls.");

        var engine = new EngineInstallContext(install);
        try
        {
            AssertEveryShapeResolves(engine);
        }
        finally
        {
            engine.Unload();
        }
    }

    /// <summary>Runs every <see cref="EngineCompat"/> and signal resolver against one install's
    /// loaded types.</summary>
    /// <param name="engine">The install's load context.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertEveryShapeResolves(EngineInstallContext engine)
    {
        Type gameVersionType = engine.Type("Vintagestory.API.Config.GameVersion");

        // The whole point of the load context: these are the install's types, not the ones this
        // assembly was compiled against (same file for the VINTAGE_STORY row, different identity).
        Assert.NotSame(typeof(Vintagestory.API.Config.GameVersion), gameVersionType);

        string version = EngineCompat.ReadVersionConstant(gameVersionType, "ShortGameVersion");
        Assert.NotEmpty(EngineCompat.ReadVersionConstant(gameVersionType, "NetworkVersion"));
        EngineCompat.CheckSupportedFloor(version);

        // The exit lifecycle: exitState/GameExitState on 1.22+, exit/GameExit before.
        Type serverType = engine.Type("Vintagestory.Server.ServerMain");
        FieldInfo exitField = EngineCompat.ResolveExitStateField(serverType, version);
        Assert.Contains(exitField.Name, ExitStateFieldNames);
        Assert.NotNull(EngineCompat.StopBinding.Resolve(serverType, version));

        // Playing is 3 before 1.22 and 4 since (Admitted was inserted ahead of it).
        Assert.NotNull(EngineCompat.ParseEnumMember(
            engine.Type("Vintagestory.API.Server.EnumClientState"), "Playing", version, Consequence));

        // Fields before 1.22, properties since.
        Type entityType = engine.Type("Vintagestory.API.Common.Entities.Entity");
        Assert.NotNull(EngineCompat.ResolveInstanceReader(entityType, "Pos", version, Consequence));
        Assert.NotNull(EngineCompat.ResolveInstanceReader(entityType, "ServerPos", version, Consequence));

        Type channelType = engine.Type("Vintagestory.Common.NetworkChannelBase");
        AssertField(channelType, "channelId", typeof(int), version);
        AssertField(channelType, "messageTypes", typeof(Dictionary<Type, int>), version);

        // Groupid and ChatType are public fields Atlas reads directly, like Message already is
        // (no EngineCompat indirection needed for a public member); pinned here so a rename
        // shows up on every supported install, not only the one Atlas compiles against.
        Type chatLineType = engine.Type("Packet_ChatLine");
        Assert.NotNull(chatLineType.GetField("Groupid", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(chatLineType.GetField("ChatType", BindingFlags.Public | BindingFlags.Instance));

        AssertClientObservationShapes(engine, version);
        AssertClientListenerShapes(engine, serverType, version);

        // The dummy connection's inbound queue, reached through an engine-owned field type: it
        // must come from this context, which is why the resolver takes the type as an argument.
        Type dummyNetworkType = engine.Type("Vintagestory.Common.DummyNetwork");
        AssertField(engine.Type("Vintagestory.Server.DummyTcpNetServer"), "network", dummyNetworkType, version);
        AssertField(dummyNetworkType, "ServerReceiveBuffer", typeof(Queue<object>), version);

        // The shared UDP queue Atlas empties on every pass (SharedUdpDrain), under the engine's own
        // lock for it: the server that owns the network, the queue, and the lock's type.
        AssertField(engine.Type("Vintagestory.Server.Network.DummyUdpNetServer"), "network", dummyNetworkType, version);
        AssertField(dummyNetworkType, "ClientReceiveBuffer", typeof(Queue<object>), version);
        AssertField(dummyNetworkType, "ClientReceiveBufferLock", engine.Type("Vintagestory.Client.NoObf.MonitorObject"), version);
        AssertPacketIdOnTheWire(engine, version);

        // The two signals the host degrades on rather than failing: the assets-build box and the
        // entity-simulation tick stamp. Their live shells resolve the owning member first, so the
        // row pins that member too.
        FieldInfo? assetsBoxField = AssetsBuildSignal.ResolveServerBoxField(serverType);
        Assert.True(assetsBoxField != null, "'ServerMain.serverAssetsPacket' is gone from this engine.");
        Assert.NotNull(AssetsBuildSignal.ResolveBoxFields(assetsBoxField.FieldType));
        Assert.True(
            SimulationTickSignal.ResolveSystemsField(serverType) != null, "'ServerMain.Systems' is gone from this engine.");
        Assert.NotNull(SimulationTickSignal.ResolveStampField(
            engine.Type("Vintagestory.Server." + SimulationTickSignal.EntitySimulationTypeName)));

        // Rollback's three internals, resolved by the same EngineCompat resolvers as the rest.
        // Nothing else checks them: their absence does not fail a boot, it silently degrades
        // rollback to a full host recycle on whichever version drifted.
        Type chunkThreadType = engine.Type(typeof(ChunkServerThread).FullName!);
        AssertField(serverType, "chunkThread", chunkThreadType, version);
        AssertField(chunkThreadType, "gameDatabase", engine.Type(typeof(GameDatabase).FullName!), version);

        // The binder behind a name-only lookup widens, so it would also hand back a method taking
        // an int where rollback passes a long; the resolver compares the signature it picked.
        Type[] discardSignature =
        [
            typeof(long),
            engine.Type(typeof(ChunkPos).FullName!),
            engine.Type(typeof(ServerChunk).FullName!),
            typeof(List<>).MakeGenericType(engine.Type(typeof(ServerChunkWithCoord).FullName!)),
            serverType,
        ];
        Assert.NotNull(EngineCompat.ResolveStaticMethod(
            engine.Type("Vintagestory.Server.ServerSystemUnloadChunks"),
            "TryUnloadChunk",
            discardSignature,
            version,
            Consequence));
    }

    /// <summary>Pins every engine member the client observations read straight off a decoded
    /// packet, and the tick listener registration the per-pass drain rides. All are public and
    /// compiled against, so drift would fail at JIT on a prebuilt binary rather than at a
    /// resolver; this row is what makes it show up on every supported install first.</summary>
    /// <param name="engine">The install's load context.</param>
    /// <param name="version">The install's short game version, for the failure messages.</param>
    private static void AssertClientObservationShapes(EngineInstallContext engine, string version)
    {
        Type entity = engine.Type("Packet_Entity");
        Type spawn = engine.Type("Packet_EntitySpawn");
        Type entities = engine.Type("Packet_Entities");
        Type despawn = engine.Type("Packet_EntityDespawn");
        Type playerData = engine.Type("Packet_PlayerData");
        Type groups = engine.Type("Packet_PlayerGroups");
        Type group = engine.Type("Packet_PlayerGroup");

        // The envelope: which sub-message a packet carries is how the drain dispatches (see
        // ClientObservations), and Id is only read to name a packet that failed to decode.
        Type server = engine.Type("Packet_Server");
        AssertPublicField(server, "Id", typeof(int), version);
        AssertPublicField(server, "Entity", entity, version);
        AssertPublicField(server, "EntitySpawn", spawn, version);
        AssertPublicField(server, "Entities", entities, version);
        AssertPublicField(server, "EntityDespawn", despawn, version);
        AssertPublicField(server, "PlayerData", playerData, version);
        AssertPublicField(server, "PlayerGroups", groups, version);
        AssertPublicField(server, "PlayerGroup", group, version);

        AssertPublicField(entity, "EntityId", typeof(long), version);
        AssertPublicField(entity, "EntityType", typeof(string), version);

        // The batch arrays are sized by the engine's growth: only the first Count entries are real.
        AssertPublicField(spawn, "Entity", entity.MakeArrayType(), version);
        AssertPublicField(spawn, "EntityCount", typeof(int), version);
        AssertPublicField(entities, "Entities", entity.MakeArrayType(), version);
        AssertPublicField(entities, "EntitiesCount", typeof(int), version);

        // Packet 36: parallel arrays, one entry per entity, sized by the same growth. Only the
        // first EntityIdCount ids are real, and a reason is read only where its own count says
        // the packet carries one.
        AssertPublicField(despawn, "EntityId", typeof(long[]), version);
        AssertPublicField(despawn, "EntityIdCount", typeof(int), version);
        AssertPublicField(despawn, "DespawnReason", typeof(int[]), version);
        AssertPublicField(despawn, "DespawnReasonCount", typeof(int), version);

        AssertPublicField(playerData, "PlayerUID", typeof(string), version);
        AssertPublicField(playerData, "PlayerName", typeof(string), version);
        AssertPublicField(playerData, "EntityId", typeof(long), version);
        AssertPublicField(playerData, "ClientId", typeof(int), version);
        AssertPublicField(playerData, "GameMode", typeof(int), version);

        AssertPublicField(groups, "Groups", group.MakeArrayType(), version);
        AssertPublicField(groups, "GroupsCount", typeof(int), version);
        AssertPublicField(group, "Uid", typeof(int), version);
        AssertPublicField(group, "Name", typeof(string), version);
        AssertPublicField(group, "Owneruid", typeof(string), version);
        AssertPublicField(group, "Membership", typeof(int), version);

        // All three enums are cast straight from the packet's int, like EnumChatType: their member
        // order is the contract, so a reordering or an insertion shows up here.
        AssertEnumMembers(engine.Type("Vintagestory.API.Common.EnumGameMode"), ["Guest", "Survival", "Creative", "Spectator"], version);
        AssertEnumMembers(engine.Type("Vintagestory.API.Common.EnumPlayerGroupMemberShip"), ["None", "Member", "Op", "Owner"], version);
        AssertEnumMembers(
            engine.Type("Vintagestory.API.Common.EnumDespawnReason"),
            ["Death", "Combusted", "OutOfRange", "PickedUp", "Unload", "Disconnect", "Expire", "Removed"],
            version);

        // The per-pass listener registers with an error handler (the two-argument overload would
        // let a throw abort the rest of the pass) and unregisters by id. UnregisterEventBusListener
        // is deliberately not pinned: it only exists from 1.22 on, and Atlas looks it up.
        Type events = engine.Type("Vintagestory.API.Common.IEventAPI");
        MethodInfo? register = events.GetMethod(
            "RegisterGameTickListener", [typeof(Action<float>), typeof(Action<Exception>), typeof(int), typeof(int)]);
        Assert.True(
            register != null && register.ReturnType == typeof(long),
            $"IEventAPI.RegisterGameTickListener(Action<float>, Action<Exception>, int, int) returning long is gone from {version}.");
        Assert.NotNull(events.GetMethod("UnregisterGameTickListener", [typeof(long)]));
    }

    /// <summary>Pins every engine member the opt-in loopback listener and the character gate are
    /// written against: the sockets, the config, the client table, the dummy-connection flag, and
    /// the public API the gate's handler and the bridge's early registration use. The listener
    /// names them directly, so on a prebuilt binary drift would fail at JIT; this row, and
    /// <see cref="EngineCompat.ValidateClientListener"/> behind the opt-in, make it show up as a
    /// named symbol first.</summary>
    /// <param name="engine">The install's load context.</param>
    /// <param name="serverType">The install's <c>ServerMain</c>.</param>
    /// <param name="version">The install's short game version, for the failure messages.</param>
    private static void AssertClientListenerShapes(EngineInstallContext engine, Type serverType, string version)
    {
        EngineCompat.CheckClientListenerShape(serverType, version);

        // The flag that tells a test player's connection from a real one: a field before and
        // after 1.22 today, read through the field-or-property resolver.
        Assert.NotNull(EngineCompat.ResolveInstanceReader(
            engine.Type("Vintagestory.Server.ConnectedClient"), "IsSinglePlayerClient", version, Consequence));

        // Both sockets carry the port the engine binds; a client reaches slot 1 of each array.
        Assert.NotNull(engine.Type("Vintagestory.Server.TcpNetServer").GetMethod("Dispose", Type.EmptyTypes));
        Assert.NotNull(engine.Type("Vintagestory.Server.Network.UdpNetServer").GetMethod("Dispose", Type.EmptyTypes));

        // The gate's side of the public API: the PlayerJoin event the bridge registers on, the
        // mod data pair the survival mod reads its flag through, and the player's client id.
        Type events = engine.Type("Vintagestory.API.Server.IServerEventAPI");
        Assert.NotNull(events.GetEvent("PlayerJoin"));
        Type player = engine.Type("Vintagestory.API.Server.IServerPlayer");
        Assert.NotNull(player.GetMethod("SetModdata", [typeof(string), typeof(byte[])]));
        Assert.NotNull(player.GetMethod("GetModdata", [typeof(string)]));
        Assert.NotNull(engine.Type("Vintagestory.API.Common.IPlayer").GetProperty("ClientId", typeof(int)));
        Assert.NotNull(player.GetProperty("ConnectionState"));
        Assert.NotNull(engine.Type("Vintagestory.API.Util.SerializerUtil").GetMethod("Serialize", 1, [Type.MakeGenericMethodParameter(0)]));
    }

    /// <summary>Pins, on the install's own serializer, where <c>Packet_Server.Id</c> sits in the
    /// bytes the dummy connection carries: first, as field 90 (the key <c>D0 05</c>) and a varint,
    /// and left out when it is the default, 1. The drop-at-dequeue filter of the client
    /// observations reads the id that way instead of deserializing the packet
    /// (<see cref="Atlas.Internal.Player.ServerPacketId"/>).</summary>
    /// <param name="engine">The install's load context.</param>
    /// <param name="version">The install's short game version, for the failure messages.</param>
    private static void AssertPacketIdOnTheWire(EngineInstallContext engine, string version)
    {
        Type packet = engine.Type("Packet_Server");
        MethodInfo? serialize = engine.Type("Packet_ServerSerializer").GetMethod("SerializeToBytes", [packet]);
        Assert.True(serialize != null, $"Packet_ServerSerializer.SerializeToBytes(Packet_Server) is gone from {version}.");
        FieldInfo idField = packet.GetField("Id", BindingFlags.Public | BindingFlags.Instance)!;

        foreach (int id in new[] { 8, 33, 36, 55, 61, 127, 128, 300 })
        {
            object instance = Activator.CreateInstance(packet)!;
            idField.SetValue(instance, id);
            var bytes = (byte[])serialize.Invoke(null, [instance])!;

            Assert.True(
                Atlas.Internal.Player.ServerPacketId.TryRead(bytes, bytes.Length, out int read) && read == id,
                $"Packet_Server.Id {id} is not the first field of the serialized packet on {version}: {Convert.ToHexString(bytes)}.");
        }

        object identification = Activator.CreateInstance(packet)!;
        idField.SetValue(identification, 1);
        var omitted = (byte[])serialize.Invoke(null, [identification])!;
        Assert.False(
            Atlas.Internal.Player.ServerPacketId.TryRead(omitted, omitted.Length, out _),
            $"Packet_Server.Id 1, the default, is no longer left out of the serialized packet on {version}.");
    }

    private static void AssertPublicField(Type declaring, string name, Type fieldType, string version)
    {
        FieldInfo? field = declaring.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(field != null, $"'{declaring.Name}.{name}' is gone from {version}.");
        Assert.True(
            field.FieldType.FullName == fieldType.FullName,
            $"'{declaring.Name}.{name}' is a {field.FieldType} on {version}, not a {fieldType}.");
    }

    private static void AssertEnumMembers(Type enumType, string[] expectedInOrder, string version)
        => Assert.True(
            Enum.GetNames(enumType).SequenceEqual(expectedInOrder),
            $"{enumType.Name} on {version} is [{string.Join(", ", Enum.GetNames(enumType))}], not [{string.Join(", ", expectedInOrder)}].");

    private static void AssertField(Type declaring, string name, Type fieldType, string version)
        => Assert.NotNull(EngineCompat.ResolveNonPublicInstanceField(
            declaring, name, fieldType, version, Consequence));

    /// <summary>Loads one install's assemblies in isolation from the install the suite was
    /// compiled against: everything the install ships (its root and its <c>Lib</c> folder) is
    /// loaded here, and only framework assemblies fall through to the default context.</summary>
    private sealed class EngineInstallContext : AssemblyLoadContext
    {
        private readonly string _install;
        private readonly Assembly[] _engine;

        public EngineInstallContext(string install)
            : base(isCollectible: true)
        {
            _install = install;
            _engine =
            [
                LoadFromAssemblyPath(Path.Combine(install, "VintagestoryAPI.dll")),
                LoadFromAssemblyPath(Path.Combine(install, "VintagestoryLib.dll")),
            ];
        }

        /// <summary>Resolves one engine type by full name from this install.</summary>
        /// <param name="fullName">The type's namespace-qualified name.</param>
        /// <returns>The loaded type.</returns>
        public Type Type(string fullName)
        {
            Type? type = _engine.Select(assembly => assembly.GetType(fullName)).FirstOrDefault(t => t != null);
            Assert.True(type != null, $"Engine type '{fullName}' is gone from '{_install}'.");
            return type!;
        }

        /// <inheritdoc/>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = new[] { _install, Path.Combine(_install, "Lib") }
                .Select(dir => Path.Combine(dir, assemblyName.Name + ".dll"))
                .FirstOrDefault(File.Exists);
            return path == null ? null : LoadFromAssemblyPath(path);
        }
    }
}
