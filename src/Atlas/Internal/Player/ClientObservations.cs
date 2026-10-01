using System.Reflection;
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

/// <summary>Takes one test player's dummy client connection's packets out on every server pass,
/// stamps them, and decodes what the server sent when a scenario reads, exposed as
/// <see cref="IClientObservations"/>.</summary>
/// <remarks><para>The tap point (verified by decompile on 1.21.7, 1.22.0 and 1.22.7): every
/// server-to-client TCP send ends in <c>DummyNetConnection.Send</c> or
/// <c>SendPreparedPacket</c>, which enqueue the serialized <c>Packet_Server</c> bytes, never
/// compressed for a singleplayer-type client, into the shared <c>DummyNetwork</c>'s client
/// receive buffer under the engine's own lock. <c>DummyTcpNetClient.ReadMessage()</c>, the exact
/// call a real client's network loop makes, dequeues them under the same lock; nothing else ever
/// reads that buffer, so draining it here on the game thread is both race-free and the only
/// consumer. <c>SendPacketFast</c>'s in-process shortcut (<c>SendServerPacketDirectly</c>) is a
/// no-op without a client process and falls through to the same serialized send.</para>
/// <para>Two steps, so that a packet is dated by when it arrived and decoded only when somebody
/// asks. A game-tick listener registered per player (<see cref="OnPass"/>, interval 1, so once
/// per pass) only dequeues: it parks each message's bytes with the tick of the pass and the next
/// sequence number, with no deserialization and no allocation of its own beyond the list's
/// growth (the engine's <c>ReadMessage</c> allocates the message wrapper, as it always did). A
/// read (<see cref="Drain"/>) parks whatever is still in the engine queue (stamped with the tick
/// of the read), then decodes the parked list in order. Decoding in the listener would put every
/// decode bug, and the cost of every chunk packet, inside the server pass: a throw in a tick
/// listener aborts the rest of that pass's listeners and, when it persists, repeats on every pass.
/// Measured against the alternatives in the 0.16 drain decision: dequeue-only adds about 1
/// microsecond and under 0.25 KB per pass for three players, below the resolution of
/// <c>MeasureTicks</c>.</para>
/// <para>A tick is exact to one pass. The listener registers after the harness's own tick
/// listener (the bridge registers at boot, this one at the end of <c>JoinPlayer</c>), so in a pass
/// the harness count has already advanced when this listener stamps: a message sent between two
/// passes, from the main-thread task queue, or by a listener registered after this one is stamped
/// with the next tick; one sent by an earlier listener, or read before the next pass, with the
/// current one.</para>
/// <para>Packets are dispatched on which sub-message they carry, not on <c>Packet_Server.Id</c>:
/// the ids are literals in the engine's send sites (52 highlight, 61 particles, 55 custom
/// packet, 8 chat line, 33 entity, 34 entity spawn, 40 entity list, 41 player data, 49 player
/// groups, 50 player group on every supported version), not reflectable constants, and the
/// client handlers read exactly the sub-message, so its presence is the authoritative signal.
/// <see cref="EntityArrivalPath"/> carries the ids of the three entity paths as documented
/// values.</para>
/// <para>The listener empties the engine queue on every pass, so this class is its only consumer
/// in a way callers can trip over: a raw reader of the same buffer (by reflection) sees nothing,
/// and its positive control fails at once. <see cref="Packets{T}"/> is the one reader that skips
/// the drain when it throws: it resolves its channel and message type first.</para>
/// <para>Reads do not consume the captures. They accumulate from the join until
/// <see cref="Clear"/> or the restored-world hook resets them, so <see cref="Chat"/> and the
/// other readers answer "since the join or the last clear", never "since the last read".
/// <see cref="Clear"/> drops the parked bytes without decoding them. The sequence counter
/// is not reset by it, so it stays monotonic for the player's lifetime. Parked bytes are kept
/// until a read or a clear, no cap: that is what the engine queue did before this class took
/// over its draining.</para>
/// <para>A packet that cannot be decoded is dropped and reported once, by the read that meets
/// it, after the rest was decoded (see <see cref="Drain"/>). An exception that escapes the
/// listener itself (the error handler the listener is registered with) is stored and reported
/// the same way by the next read.</para>
/// <para>When the player is gone (kicked, left, removed by a rollback restore),
/// <see cref="Detach"/> removes both listeners. What was parked or decoded stays readable.</para>
/// <para>Every member runs on the game thread. The restored-world hook clears captures the way
/// a cooperating mod resyncs its own in-memory state: same event, same moment (after the
/// SaveGame restore, before any chunk column reload).</para></remarks>
internal sealed class ClientObservations : IClientObservations
{
    /// <summary>The <c>ClientId</c> the engine stamps on the player-data packet it broadcasts
    /// when a player leaves (<c>ServerMain</c>, 1.21.7 and 1.22.7).</summary>
    private const int DepartureClientId = -99;

    /// <summary><c>IEventAPI.UnregisterEventBusListener</c>, which only exists from 1.22 on (1.21.x
    /// and 1.20.x have <c>RegisterEventBusListener</c> and nothing to undo it), so it is looked up
    /// on the loaded engine instead of called: a direct call would fail with a
    /// <see cref="MissingMethodException"/> on a prebuilt binary run against the 1.21 floor. Null
    /// on those versions, where the restored hook stays registered, inert, until the host is
    /// disposed.</summary>
    private static readonly MethodInfo? UnregisterEventBus
        = typeof(IEventAPI).GetMethod("UnregisterEventBusListener", [typeof(EventBusListenerDelegate)]);

    private readonly ICoreServerAPI _api;
    private readonly Func<NetIncomingMessage?> _readMessage;
    private readonly Func<int> _tick;
    private readonly string _ownPlayerUid;
    private readonly EventBusListenerDelegate _onRestored;
    private readonly long _listenerId;
    private readonly List<ParkedMessage> _parked = [];
    private readonly Dictionary<int, HighlightedBlock[]> _highlights = [];
    private readonly List<SpawnedParticles> _particles = [];
    private readonly List<Packet_CustomPacket> _custom = [];
    private readonly List<ReceivedChatLine> _chat = [];
    private readonly List<ReceivedEntity> _entities = [];
    private readonly HashSet<long> _entityIds = [];
    private readonly List<ReceivedPlayerData> _playerData = [];
    private readonly List<ReceivedGroupListing> _groupListings = [];
    private readonly List<ReceivedGroupUpdate> _groupUpdates = [];
    private int _sequence;
    private Exception? _listenerError;
    private bool _detached;

    /// <summary>Initializes a new instance of the <see cref="ClientObservations"/> class and
    /// registers its per-pass listener and its restored-world hook.</summary>
    /// <param name="api">The live server API: particle-provider registry, network channel
    /// registry, and the event bus the restored-world hook fires on and the tick listener
    /// registers on.</param>
    /// <param name="readMessage">Dequeues the next packet from the player's dummy client
    /// connection's receive buffer, or <see langword="null"/> when it is empty
    /// (<c>DummyTcpNetClient.ReadMessage</c>).</param>
    /// <param name="tick">The harness tick, <c>TickSource.TickCount</c>: the unit the stamps are
    /// taken in. A delegate so a pure test can drive it.</param>
    /// <param name="ownPlayerUid">The uid of the player this instance observes, to tell the
    /// player-data packets about itself from the ones about others.</param>
    public ClientObservations(ICoreServerAPI api, Func<NetIncomingMessage?> readMessage, Func<int> tick, string ownPlayerUid)
    {
        _api = api;
        _readMessage = readMessage;
        _tick = tick;
        _ownPlayerUid = ownPlayerUid;
        _onRestored = OnWorldRestored;
        api.Event.RegisterEventBusListener(_onRestored, 0.5, RollbackHooks.RestoredEventName);

        // The overload with an error handler, never the two-argument one: without a handler an
        // exception escaping the listener aborts the rest of the pass's listeners.
        _listenerId = api.Event.RegisterGameTickListener(OnPass, OnPassError, 1);
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
    public IReadOnlyList<ReceivedChatLine> Chat()
    {
        Drain();
        return _chat.ToArray();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ChatLines()
    {
        Drain();

        // Array, not List<T>.ConvertAll: like every other observation here, a caller casting the
        // IReadOnlyList back cannot mutate this instance's own state through it.
        return _chat.Select(line => line.Message).ToArray();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReceivedEntity> EntityArrivals()
    {
        Drain();
        return _entities.ToArray();
    }

    /// <inheritdoc/>
    public bool HasReceivedEntity(long entityId)
    {
        Drain();
        return _entityIds.Contains(entityId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReceivedPlayerData> PlayerData()
    {
        Drain();
        return _playerData.ToArray();
    }

    /// <inheritdoc/>
    public bool HasReceivedPlayerData(string playerUid)
    {
        Drain();
        return _playerData.Exists(data => !data.IsDeparture && data.PlayerUid == playerUid);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReceivedGroupListing> GroupListings()
    {
        Drain();
        return _groupListings.ToArray();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReceivedGroupUpdate> GroupUpdates()
    {
        Drain();
        return _groupUpdates.ToArray();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        // Everything parked or still queued predates the clear: dropped undecoded, so a clear
        // never throws on a packet that would not have decoded.
        _parked.Clear();
        while (_readMessage() != null)
        {
        }

        _highlights.Clear();
        _particles.Clear();
        _custom.Clear();
        _chat.Clear();
        _entities.Clear();
        _entityIds.Clear();
        _playerData.Clear();
        _groupListings.Clear();
        _groupUpdates.Clear();
        _listenerError = null;
    }

    /// <summary>The per-pass listener: parks whatever the engine queued since the last pass,
    /// stamped with the current tick. Internal for the allocation guard test, which calls it
    /// directly.</summary>
    /// <param name="deltaTime">The engine's elapsed time since the last call, unused.</param>
    internal void OnPass(float deltaTime) => Park(_tick());

    /// <summary>Removes both listeners after one last park, once the player is verifiably gone
    /// from the server. Idempotent. The parked and decoded data stay readable, so a scenario can
    /// still assert on what a kicked player received.</summary>
    internal void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        Park(_tick());
        _api.Event.UnregisterGameTickListener(_listenerId);
        UnregisterEventBus?.Invoke(_api.Event, [_onRestored]);
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

    /// <summary>Decodes one entity header, the way the client's entity handlers read it: the type
    /// code goes through <c>AssetLocation</c> (a client builds <c>new AssetLocation(EntityType)</c>,
    /// where a missing domain means <c>game</c>) and comes out in its short form.</summary>
    /// <param name="entity">The entity header, from packet 33, 34 or 40.</param>
    /// <param name="path">The path the header arrived by.</param>
    /// <param name="tick">The arrival tick.</param>
    /// <param name="sequence">The arrival sequence number.</param>
    /// <returns>The record; an empty type when the packet carried none.</returns>
    internal static ReceivedEntity DecodeEntity(Packet_Entity entity, EntityArrivalPath path, int tick, int sequence)
    {
        string type = entity.EntityType is { Length: > 0 } code ? new AssetLocation(code).ToShortString() : string.Empty;
        return new ReceivedEntity(entity.EntityId, type, path, tick, sequence);
    }

    /// <summary>Decodes the entities of a batch packet (34, or 40): the first
    /// <paramref name="count"/> entries of an array the engine sized for the whole batch, whose
    /// tail is null.</summary>
    /// <param name="entries">The packet's entity array, or <see langword="null"/> when it has
    /// none.</param>
    /// <param name="count">The number of real entries the packet declares. Clamped to the array,
    /// and a null entry inside it is skipped.</param>
    /// <param name="path">The path the batch arrived by.</param>
    /// <param name="tick">The arrival tick, shared by every entity of the packet.</param>
    /// <param name="sequence">The arrival sequence number, shared by every entity of the
    /// packet.</param>
    /// <returns>One record per entry, in the packet's order.</returns>
    internal static ReceivedEntity[] DecodeEntities(Packet_Entity[]? entries, int count, EntityArrivalPath path, int tick, int sequence)
    {
        if (entries == null || count <= 0)
        {
            return [];
        }

        var decoded = new List<ReceivedEntity>(Math.Min(count, entries.Length));
        for (int i = 0; i < count && i < entries.Length; i++)
        {
            if (entries[i] is { } entry)
            {
                decoded.Add(DecodeEntity(entry, path, tick, sequence));
            }
        }

        return [.. decoded];
    }

    /// <summary>Decodes one player world-data packet (41).</summary>
    /// <param name="packet">The packet.</param>
    /// <param name="ownPlayerUid">The uid of the player that received it.</param>
    /// <param name="tick">The arrival tick.</param>
    /// <param name="sequence">The arrival sequence number.</param>
    /// <returns>The record. <see cref="ReceivedPlayerData.IsDeparture"/> follows the engine's own
    /// "player left" marker, a client id of -99.</returns>
    internal static ReceivedPlayerData DecodePlayerData(Packet_PlayerData packet, string ownPlayerUid, int tick, int sequence)
    {
        string uid = packet.PlayerUID ?? string.Empty;
        return new ReceivedPlayerData(
            uid,
            packet.PlayerName ?? string.Empty,
            packet.EntityId,
            packet.ClientId,
            (EnumGameMode)packet.GameMode,
            IsDeparture: packet.ClientId == DepartureClientId,
            IsSelf: string.Equals(uid, ownPlayerUid, StringComparison.Ordinal),
            tick,
            sequence);
    }

    /// <summary>Decodes one player group, as the client's group handlers read it (name, owner
    /// and membership; the chat history is not kept).</summary>
    /// <param name="packet">The group.</param>
    /// <returns>The record, with empty strings for a missing name or owner.</returns>
    internal static ReceivedPlayerGroup DecodeGroup(Packet_PlayerGroup packet)
        => new(packet.Uid, packet.Name ?? string.Empty, packet.Owneruid ?? string.Empty, (EnumPlayerGroupMemberShip)packet.Membership);

    /// <summary>Decodes a full player-groups listing (49).</summary>
    /// <param name="packet">The packet. Its group array is sized by the engine's growth, so only
    /// the first <c>GroupsCount</c> entries are real.</param>
    /// <param name="tick">The arrival tick.</param>
    /// <param name="sequence">The arrival sequence number.</param>
    /// <returns>The listing, its groups in the server's order and not writable by the
    /// caller.</returns>
    internal static ReceivedGroupListing DecodeGroupListing(Packet_PlayerGroups packet, int tick, int sequence)
    {
        var groups = new List<ReceivedPlayerGroup>();
        if (packet.Groups != null)
        {
            for (int i = 0; i < packet.GroupsCount && i < packet.Groups.Length; i++)
            {
                if (packet.Groups[i] is { } group)
                {
                    groups.Add(DecodeGroup(group));
                }
            }
        }

        return new ReceivedGroupListing(tick, sequence, groups.AsReadOnly());
    }

    /// <summary>Decodes a single-group update (50).</summary>
    /// <param name="packet">The group the packet carries.</param>
    /// <param name="tick">The arrival tick.</param>
    /// <param name="sequence">The arrival sequence number.</param>
    /// <returns>The update.</returns>
    internal static ReceivedGroupUpdate DecodeGroupUpdate(Packet_PlayerGroup packet, int tick, int sequence)
        => new(tick, sequence, DecodeGroup(packet));

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

    private void OnWorldRestored(string eventName, ref EnumHandling handling, IAttribute data)
    {
        // A player that is gone keeps what it received: after Detach the hook is inert, whether or
        // not this engine could unregister it.
        if (!_detached)
        {
            Clear();
        }
    }

    /// <summary>The tick listener's error handler: keeps the first exception that escaped the
    /// listener and does nothing else, so the next read can report it. Runs on the game
    /// thread.</summary>
    /// <param name="error">What the listener threw.</param>
    private void OnPassError(Exception error) => _listenerError ??= error;

    /// <summary>Takes everything the engine queued out of the player's receive buffer and parks
    /// it, undecoded, stamped with <paramref name="tick"/> and the next sequence numbers. Shared
    /// by the per-pass listener and the read, so the sequence is one counter.</summary>
    /// <param name="tick">The tick to stamp with.</param>
    private void Park(int tick)
    {
        NetIncomingMessage? message;
        while ((message = _readMessage()) != null)
        {
            _parked.Add(new ParkedMessage(message, tick, _sequence++));
        }
    }

    /// <summary>Decodes everything parked, oldest first, after parking what is still in the
    /// engine's queue (stamped with the tick of this read).</summary>
    /// <exception cref="InvalidOperationException">Thrown, after the rest was decoded, when a
    /// packet could not be decoded (naming its id, tick and sequence; the packet is dropped, so the
    /// next read succeeds), or else when the tick listener recorded an error since the last
    /// read.</exception>
    private void Drain()
    {
        Park(_tick());

        Exception? firstFailure = null;
        string? firstFailureDescription = null;
        int failures = 0;
        foreach (ParkedMessage parked in _parked)
        {
            int packetId = -1;
            try
            {
                Packet_Server packet = Packet_ServerSerializer.DeserializeBuffer(
                    parked.Message.message, parked.Message.messageLength, new Packet_Server());
                packetId = packet.Id;
                Apply(packet, parked.Tick, parked.Sequence);
            }
            catch (Exception ex)
            {
                // Deliberately broad: whatever a malformed packet or a provider throws belongs
                // to the read that meets it, and one bad packet must not hide the ones behind
                // it. The first is kept and thrown once the rest is decoded.
                failures++;
                if (firstFailure == null)
                {
                    firstFailure = ex;
                    firstFailureDescription = packetId < 0
                        ? $"a packet whose envelope could not be read, Tick {parked.Tick}, Sequence {parked.Sequence}"
                        : $"packet id {packetId}, Tick {parked.Tick}, Sequence {parked.Sequence}";
                }
            }
        }

        _parked.Clear();

        if (firstFailure != null)
        {
            string more = failures > 1 ? $" and {failures - 1} more packet(s) failed after it" : string.Empty;
            throw new InvalidOperationException(
                $"A packet the server sent to this test player could not be decoded: {firstFailureDescription}{more}. " +
                "It is dropped and everything behind it was kept, so the next read succeeds; see the inner exception.",
                firstFailure);
        }

        if (_listenerError is { } listenerError)
        {
            _listenerError = null;
            throw new InvalidOperationException(
                "The per-pass drain of this test player's received packets failed inside the server's tick listener; " +
                "see the inner exception. The packets it had parked before failing were kept.",
                listenerError);
        }
    }

    private void Apply(Packet_Server packet, int tick, int sequence)
    {
        // Anything that is none of the kinds below falls through and is dropped: draining
        // consumed it, and nothing else can read it afterwards.
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
            _chat.Add(new ReceivedChatLine(line.Message, (EnumChatType)line.ChatType, line.Groupid));
        }
        else if (packet.Entity is { } entity)
        {
            AddEntities([DecodeEntity(entity, EntityArrivalPath.TrackedRange, tick, sequence)]);
        }
        else if (packet.EntitySpawn is { } spawn)
        {
            AddEntities(DecodeEntities(spawn.Entity, spawn.EntityCount, EntityArrivalPath.Spawn, tick, sequence));
        }
        else if (packet.Entities is { } list)
        {
            AddEntities(DecodeEntities(list.Entities, list.EntitiesCount, EntityArrivalPath.JoinList, tick, sequence));
        }
        else if (packet.PlayerData is { } playerData)
        {
            _playerData.Add(DecodePlayerData(playerData, _ownPlayerUid, tick, sequence));
        }
        else if (packet.PlayerGroups is { } groups)
        {
            _groupListings.Add(DecodeGroupListing(groups, tick, sequence));
        }
        else if (packet.PlayerGroup is { } group)
        {
            _groupUpdates.Add(DecodeGroupUpdate(group, tick, sequence));
        }
    }

    private void AddEntities(IReadOnlyCollection<ReceivedEntity> arrivals)
    {
        foreach (ReceivedEntity arrival in arrivals)
        {
            _entities.Add(arrival);
            _entityIds.Add(arrival.EntityId);
        }
    }

    /// <summary>One packet taken out of the receive buffer: its bytes, and where it fell.</summary>
    private readonly record struct ParkedMessage(NetIncomingMessage Message, int Tick, int Sequence);
}
