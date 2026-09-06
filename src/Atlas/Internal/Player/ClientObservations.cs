using Atlas.Api;
using Atlas.Internal.Bootstrap;
using Atlas.Internal.Rollback;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Common;

namespace Atlas.Internal.Player;

/// <summary>Drains one test player's dummy client connection and decodes what the server sent,
/// exposed as <see cref="IClientObservations"/>.</summary>
/// <remarks><para>The tap point (verified by decompile on 1.21.7, 1.22.0 and 1.22.7): every
/// server-to-client TCP send ends in <c>DummyNetConnection.Send</c> or
/// <c>SendPreparedPacket</c>, which enqueue the serialized <c>Packet_Server</c> bytes, never
/// compressed for a singleplayer-type client, into the shared <c>DummyNetwork</c>'s client
/// receive buffer under the engine's own lock. <c>DummyTcpNetClient.ReadMessage()</c>, the exact
/// call a real client's network loop makes, dequeues them under the same lock; nothing else ever
/// reads that buffer, so draining it here on the game thread is both race-free and the only
/// consumer. <c>SendPacketFast</c>'s in-process shortcut (<c>SendServerPacketDirectly</c>) is a
/// no-op without a client process and falls through to the same serialized send.</para>
/// <para>Packets are dispatched on which sub-message they carry, not on <c>Packet_Server.Id</c>:
/// the ids are literals in the engine's send sites (52 highlight, 61 particles, 55 custom
/// packet, 8 chat line on every supported version), not reflectable constants, and the client
/// handlers read exactly the sub-message, so its presence is the authoritative signal.</para>
/// <para>Every member runs on the game thread. The restored-world hook clears captures the way
/// a cooperating mod resyncs its own in-memory state: same event, same moment (after the
/// SaveGame restore, before any chunk column reload).</para></remarks>
internal sealed class ClientObservations : IClientObservations
{
    private readonly ICoreServerAPI _api;
    private readonly DummyTcpNetClient _client;
    private readonly Dictionary<int, HighlightedBlock[]> _highlights = [];
    private readonly List<SpawnedParticles> _particles = [];
    private readonly List<Packet_CustomPacket> _custom = [];
    private readonly List<string> _chat = [];

    /// <summary>Initializes a new instance of the <see cref="ClientObservations"/> class.</summary>
    /// <param name="api">The live server API: particle-provider registry, network channel
    /// registry, and the event bus the restored-world hook fires on.</param>
    /// <param name="client">The player's dummy client connection, whose receive buffer holds
    /// the server's outbound packets.</param>
    public ClientObservations(ICoreServerAPI api, DummyTcpNetClient client)
    {
        _api = api;
        _client = client;
        api.Event.RegisterEventBusListener(OnWorldRestored, 0.5, RollbackHooks.RestoredEventName);
    }

    /// <inheritdoc/>
    public IReadOnlyList<HighlightedBlock> Highlights(int slot)
    {
        Drain();
        return _highlights.TryGetValue(slot, out HighlightedBlock[]? blocks) ? blocks : [];
    }

    /// <inheritdoc/>
    public IReadOnlyList<SpawnedParticles> Particles()
    {
        Drain();
        return _particles.ToArray();
    }

    /// <inheritdoc/>
    public IReadOnlyList<T> Packets<T>(string channel)
    {
        (int channelId, int messageId) = ResolveChannelMessage(_api.Network.GetChannel(channel), channel, typeof(T));
        Drain();
        var messages = new List<T>();
        foreach (Packet_CustomPacket packet in _custom)
        {
            if (packet.ChannelId == channelId && packet.MessageId == messageId)
            {
                messages.Add(Deserialize<T>(packet.Data));
            }
        }

        return messages;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ChatLines()
    {
        Drain();
        return _chat.ToArray();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        while (_client.ReadMessage() != null)
        {
            // Discard undecoded packets too: they predate the clear.
        }

        _highlights.Clear();
        _particles.Clear();
        _custom.Clear();
        _chat.Clear();
    }

    /// <summary>Decodes one highlight packet into the slot it targets and its blocks, with the
    /// client's own per-position color rule (<c>BlockHighlight.TesselateArbitraryModel</c>): one
    /// color per position only when at least as many colors as positions were sent and more than
    /// one, otherwise the first color for every position, 0 when none were sent.</summary>
    /// <param name="packet">The highlight packet.</param>
    /// <returns>The slot id and its blocks; no blocks when the packet carried no positions,
    /// which is how a client clears the slot's highlight.</returns>
    internal static (int Slot, HighlightedBlock[] Blocks) DecodeHighlight(Packet_HighlightBlocks packet)
    {
        if (packet.Blocks.Length == 0)
        {
            return (packet.Slotid, []);
        }

        BlockPos[] positions = BlockTypeNet.UnpackBlockPositions(packet.Blocks);
        int colorsCount = packet.ColorsCount;
        bool perPosition = colorsCount >= positions.Length && colorsCount > 1;

        // The color every position falls back to when the packet did not send one per position:
        // the first color sent, or 0 when none was.
        int sharedColor = colorsCount > 0 ? packet.Colors[0] : 0;
        var blocks = new HighlightedBlock[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            blocks[i] = new HighlightedBlock(positions[i], perPosition ? packet.Colors[i] : sharedColor);
        }

        return (packet.Slotid, blocks);
    }

    /// <summary>Decodes one particle packet the way the client's <c>HandleSpawnParticles</c>
    /// does: instantiate the provider by its registered class name, then let the provider read
    /// its own bytes back.</summary>
    /// <param name="packet">The particle packet.</param>
    /// <param name="createProvider">The class-name-to-provider factory
    /// (<c>IClassRegistryAPI.CreateParticlePropertyProvider</c>).</param>
    /// <param name="world">The world the provider resolves blocks and items against, when its
    /// color is texture-driven.</param>
    /// <returns>The decoded spawn.</returns>
    internal static SpawnedParticles DecodeParticles(
        Packet_SpawnParticles packet,
        System.Func<string, IParticlePropertiesProvider> createProvider,
        IWorldAccessor world)
    {
        IParticlePropertiesProvider provider = createProvider(packet.ParticlePropertyProviderClassName);
        using var reader = new BinaryReader(new MemoryStream(packet.Data));
        provider.FromBytes(reader, world);
        if (provider is SimpleParticleProperties simple)
        {
            return new SpawnedParticles(
                packet.ParticlePropertyProviderClassName, provider, simple.MinPos, simple.MinVelocity, simple.MinQuantity, simple.Color);
        }

        Vec3d position = provider.Pos;
        return new SpawnedParticles(
            packet.ParticlePropertyProviderClassName, provider, position, provider.GetVelocity(position), provider.Quantity, 0);
    }

    /// <summary>Resolves a channel name and message type to the ids the server stamps on the
    /// wire, through the server's own channel registry (both sides register symmetrically, so
    /// the server knows every name and type the client would).</summary>
    /// <param name="registered">The server channel registered under <paramref name="channel"/>,
    /// or <see langword="null"/> when none is.</param>
    /// <param name="channel">The channel name, for the diagnostics.</param>
    /// <param name="messageType">The message type to look up, matched by full name.</param>
    /// <returns>The channel id and message id.</returns>
    /// <exception cref="ArgumentException">Thrown when the channel or the type is not registered.</exception>
    internal static (int ChannelId, int MessageId) ResolveChannelMessage(
        IServerNetworkChannel? registered, string channel, Type messageType)
    {
        if (registered == null)
        {
            throw new ArgumentException(
                $"No server network channel named '{channel}' is registered: the mod's server side " +
                "must register it (IServerNetworkAPI.RegisterChannel) in StartServerSide. UDP " +
                "channels (RegisterUdpChannel) are not captured.",
                nameof(channel));
        }

        // Matched by full name, not Type identity: the game's ModLoader loads the staged mod dll
        // itself, and a scenario assembly referencing the mod project may hold its own copy of
        // the type.
        IReadOnlyDictionary<Type, int> messageTypes = EngineCompat.MessageTypesOf(registered);
        foreach ((Type type, int id) in messageTypes)
        {
            if (type.FullName == messageType.FullName)
            {
                return (EngineCompat.ChannelIdOf(registered), id);
            }
        }

        throw new ArgumentException(
            $"Message type '{messageType.FullName}' is not registered on server network channel " +
            $"'{channel}' (registered: {string.Join(", ", messageTypes.Keys.Select(t => t.FullName))}): " +
            "the mod's server side must RegisterMessageType<T>() it on the channel.");
    }

    /// <summary>Deserializes a channel message the way the client's channel handler does.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="data">The message bytes, or <see langword="null"/> for a bodyless send.</param>
    /// <returns>The message, or the type's default for a bodyless send.</returns>
    private static T Deserialize<T>(byte[]? data)
    {
        if (data == null)
        {
            return default!;
        }

        using var stream = new MemoryStream(data);
        return ProtoBuf.Serializer.Deserialize<T>(stream);
    }

    private void OnWorldRestored(string eventName, ref EnumHandling handling, IAttribute data) => Clear();

    private void Drain()
    {
        NetIncomingMessage? message;
        while ((message = _client.ReadMessage()) != null)
        {
            Apply(Packet_ServerSerializer.DeserializeBuffer(message.message, message.messageLength, new Packet_Server()));
        }
    }

    private void Apply(Packet_Server packet)
    {
        if (packet.HighlightBlocks is { } highlight)
        {
            (int slot, HighlightedBlock[] blocks) = DecodeHighlight(highlight);
            _highlights[slot] = blocks;
        }
        else if (packet.SpawnParticles is { } particles)
        {
            _particles.Add(DecodeParticles(particles, _api.ClassRegistry.CreateParticlePropertyProvider, _api.World));
        }
        else if (packet.CustomPacket is { } custom)
        {
            _custom.Add(custom);
        }
        else if (packet.Chatline is { } line)
        {
            _chat.Add(line.Message);
        }
    }
}
