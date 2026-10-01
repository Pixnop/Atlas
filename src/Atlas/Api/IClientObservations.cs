namespace Atlas.Api;

/// <summary>The subset of what the server sent to one test player that Atlas decodes (block
/// highlights, particles, mod-channel packets, chat lines, entity arrivals, player world data
/// and player-group listings): the client-side assertion surface for mods whose server side
/// drives effects on the client without a client process, and for server code that decides what
/// a client may learn. Every other packet is dropped.</summary>
/// <remarks><para>A test player's connection receives every packet a real client would; nothing
/// renders it, so the bytes wait in the connection's receive buffer. Atlas empties that buffer
/// on every server pass, with a tick listener per joined test player that takes the packets out:
/// it drops at once every packet of a kind Atlas does not decode (chunks, entity positions and
/// attributes, and the rest of what a client is sent) and stamps each of the others with the tick
/// it found it in. The decoding, with the engine's own packet serializer, happens when the
/// scenario reads this surface. Every member runs on the game thread, like the rest of
/// <see cref="ITestPlayer"/>. A send enqueues its packet in the
/// receive buffer synchronously, so a server call followed by a read on the same tick sees it
/// (no ticks needed), as long as the engine actually sends it: particles, for instance, only go
/// to players whose chunk at the spawn position was already streamed.</para>
/// <para>The drain is exclusive. Atlas is the only consumer of the receive buffer
/// (<c>DummyTcpNetClient.ReadMessage</c>), and it empties it on every pass whether or not
/// anything reads this surface. A second reader of that buffer, such as a helper that peeks at
/// it by reflection, therefore sees nothing, ever: an assertion there that a packet is absent
/// passes for the wrong reason, and only a positive control, a case where the packet is known to
/// arrive, tells the two apart (it fails at once). Read what a client received through
/// <see cref="EntityArrivals"/>, <see cref="PlayerData"/>, <see cref="GroupListings"/>,
/// <see cref="GroupUpdates"/>, <see cref="Chat"/> and <see cref="Packets{T}"/> instead.
/// (<see cref="Packets{T}"/> validates its channel and message type before it reads, so a call
/// that throws decodes nothing.)</para>
/// <para><b>Tick and Sequence.</b> Every entity, player-data and group record carries the
/// <c>Tick</c> it arrived at and a <c>Sequence</c>. <c>Tick</c> is the value of
/// <see cref="IWorldSession.CurrentTick"/> during the pass whose drain found the packet in the
/// receive buffer, or at the read when a read found it first. It is an arrival stamp, exact to
/// one pass: a packet sent at tick T carries T or T + 1. It carries T + 1 when the scenario sent
/// it between two passes, or when a tick listener registered after Atlas's, or the engine's main
/// thread task queue (<c>ProcessMain</c>), sent it. It carries T when a listener registered
/// before Atlas's sent it, or when the scenario read before the next pass. <c>Sequence</c> is
/// the position in the total order the player received everything in, across every kind of
/// packet, and only grows: order by <c>Sequence</c> and use <c>Tick</c> for windows, since
/// <c>Tick</c> never decreases as <c>Sequence</c> grows but is shared by every packet of a pass.
/// The records of one packet share its <c>Sequence</c>. <c>Tick</c> restarts at 0 after a
/// <c>RestartWorld</c>, which boots a new host. The
/// four older kinds (<see cref="Highlights"/>, <see cref="Particles"/>, <see cref="Packets{T}"/>,
/// <see cref="Chat"/>) carry neither.</para>
/// <para>Observations accumulate for the player's lifetime, from the join, and a read does not
/// consume them: <see cref="Chat"/>, <see cref="ChatLines"/>, <see cref="Particles"/>,
/// <see cref="EntityArrivals"/> and the other lists return everything captured since the join or
/// the last <see cref="Clear"/>, not since the last read (a second read returns the first one's
/// lines plus whatever arrived in between), and <see cref="Highlights"/> keeps each slot's latest
/// state the same way. A scenario that wants a window calls <see cref="Clear"/> first. They are
/// also cleared by a <c>RollbackWorld</c> restore, which does what <see cref="Clear"/> does (the
/// world state rewound, stale observations would mislead). What the server sends after the
/// restore is captured normally, and measured on 1.22.3 that is little: the restored player's own
/// player data, and not the entities or the group listings, so a "never received" assertion made
/// right after a restore passes for the wrong reason. A <c>FreshWorld</c> recycle joins new
/// players on a new host, so nothing carries over there either. A player that is gone (kicked or
/// left) keeps what it received, readable and frozen: nothing changes its observations
/// afterwards, a later restore included. A player the restore itself removes is cleared by it
/// like the others, and frozen after. Only the TCP stream is captured: packets sent over a UDP
/// mod channel (<c>RegisterUdpChannel</c>) are not observed.</para>
/// <para>A packet that fails to decode does not hide the ones behind it. The read that meets it
/// throws one <see cref="InvalidOperationException"/> naming the packet, its <c>Tick</c> and its
/// <c>Sequence</c>, after decoding the rest, and the failed packet is dropped, so the next read
/// succeeds.</para>
/// <para>What a scenario that never reads holds is bounded by what it could read. The packets
/// Atlas does not decode are dropped as they arrive, and the UDP queue every test player shares
/// is emptied on every pass (nothing in it is observable; UDP mod channels are not captured). The
/// kinds Atlas does decode accumulate until a read or <see cref="Clear"/>, as the lists above
/// say, so a long scenario that never reads should call <see cref="Clear"/> now and then. The
/// mod-channel packets are the ones to watch: they are kept for every channel, the game's own
/// included, and on vanilla those send about one packet per pass while animated entities are near
/// the player (measured: about 150 bytes per packet with 100 hens nearby, a few hundred KB over 3000
/// passes).</para></remarks>
public interface IClientObservations
{
    /// <summary>Gets the highlight slot's current blocks: the positions and colors of the LAST
    /// <c>HighlightBlocks</c> packet the server sent for <paramref name="slot"/>, mirroring the
    /// client, which replaces the slot's highlight on every packet. Empty when the last packet
    /// carried no positions (the way a mod clears a slot) or none was ever sent.</summary>
    /// <param name="slot">The highlight slot id the mod passes to <c>HighlightBlocks</c>.</param>
    /// <returns>The slot's blocks, in the order the server sent them.</returns>
    IReadOnlyList<HighlightedBlock> Highlights(int slot);

    /// <summary>Gets every particle spawn the server sent to the player, oldest first.</summary>
    /// <returns>The spawns captured since the join or the last clear, not since the last read.</returns>
    IReadOnlyList<SpawnedParticles> Particles();

    /// <summary>Gets every packet of type <typeparamref name="T"/> the server sent to the player
    /// on the mod network channel <paramref name="channel"/>, oldest first, deserialized with the
    /// same protobuf serializer the engine uses for channel messages.</summary>
    /// <typeparam name="T">The message type the mod registered on the channel
    /// (<c>RegisterMessageType&lt;T&gt;()</c>). Matched by full type name, so the scenario's copy of
    /// the mod's type is fine even when the game's ModLoader loaded the mod dll separately.</typeparam>
    /// <param name="channel">The channel name the mod's server side registered
    /// (<c>IServerNetworkAPI.RegisterChannel</c>).</param>
    /// <returns>The decoded messages captured since the join or the last clear, not since the last
    /// read.</returns>
    /// <exception cref="ArgumentException">Thrown when no server channel of that name is
    /// registered, or <typeparamref name="T"/> is not registered on it; the message names what
    /// the mod's server side must register.</exception>
    IReadOnlyList<T> Packets<T>(string channel);

    /// <summary>Gets every chat line the server sent to the player (<c>SendMessage</c>,
    /// group broadcasts, join announcements, command replies), oldest first, with its chat type
    /// and group id.</summary>
    /// <returns>The lines captured since the join or the last clear, not since the last read.</returns>
    IReadOnlyList<ReceivedChatLine> Chat();

    /// <summary>Gets every chat line the server sent to the player, oldest first, as the raw
    /// message text: a projection of <see cref="Chat"/> onto just <see cref="ReceivedChatLine.Message"/>,
    /// same order, same clearing semantics.</summary>
    /// <returns>The lines captured since the join or the last clear, not since the last read.</returns>
    IReadOnlyList<string> ChatLines();

    /// <summary>Gets every entity the server sent to the player, oldest first: one record per
    /// entity entry of every packet that carries entities, over the three paths of
    /// <see cref="EntityArrivalPath"/>, duplicates kept.</summary>
    /// <returns>The arrivals captured since the join or the last clear, not since the last
    /// read.</returns>
    /// <remarks><para>Nothing here is a promise about which entity reaches which client, only a
    /// record of what did. The engine's behavior, measured on 1.20.12 to 1.22.7, is worth knowing
    /// before asserting on it:</para>
    /// <list type="bullet">
    /// <item><description>A vanilla bug in the engine's spawn queue
    /// (<c>PhysicsManager.PrepareEntitySpawns</c>) makes packet 34 reach only the first third of
    /// the connected clients. The others get the same entity later, as packet 33. So
    /// <see cref="ReceivedEntity.Path"/> is a diagnostic: assert on the union of the paths, with
    /// <see cref="HasReceivedEntity"/>.</description></item>
    /// <item><description>Packet 33 arrives 1 to 31 passes after an entity spawned or came back
    /// into range, not in the same pass. An absence assertion needs a wait of at least that
    /// long, and a positive control that proves the observer was listening.</description></item>
    /// <item><description>Duplicates are real: an entity can arrive as 33 and 34 in the same
    /// pass, and a player's own entity arrives twice, as 40 at the join and as 33 a few passes
    /// later.</description></item>
    /// <item><description>Packet 40 depends on the version. On 1.22.x it carries only the
    /// joining player's own entity. On 1.21.x and 1.20.x it also carries the entities of the
    /// players already connected to the joiner, and re-sends a connected player's entity to the
    /// others when somebody joins.</description></item>
    /// <item><description>The order between a player's <see cref="PlayerData"/> record and its
    /// entity differs by path: player data comes first on 33 and after the entity on 34. Do not
    /// depend on it.</description></item>
    /// <item><description>After a <c>RollbackWorld</c> restore the stores are empty and the
    /// server does not send the entities again, so "never received" checked right after a
    /// restore holds for the wrong reason.</description></item>
    /// </list>
    /// <para>No entity travels in a chunk packet on 1.21.x and 1.22.x. On 1.20.x the chunk
    /// packet can carry entities, none was ever observed there, and Atlas does not decode
    /// them.</para></remarks>
    IReadOnlyList<ReceivedEntity> EntityArrivals();

    /// <summary>Gets whether the server sent the entity with the given id to the player, over
    /// any of the three paths: the union of <see cref="EntityArrivals"/>.</summary>
    /// <param name="entityId">The entity's id (<c>Entity.EntityId</c>).</param>
    /// <returns><see langword="true"/> when at least one arrival of that entity was captured
    /// since the join or the last clear.</returns>
    /// <remarks>This is the member to assert with. An absence assertion is only meaningful after
    /// a wait of at least 31 passes since the entity spawned or came into range (see
    /// <see cref="EntityArrivals"/>), with a positive control, and not right after a rollback.
    /// The recipe is on the Client-Side Testing wiki page.</remarks>
    bool HasReceivedEntity(long entityId);

    /// <summary>Gets every player world-data packet (packet 41) the server sent to the player,
    /// oldest first: the identity block for each other player, for the player itself, and the
    /// departure broadcast when a player leaves.</summary>
    /// <returns>The records captured since the join or the last clear, not since the last
    /// read.</returns>
    /// <remarks>Inventories, privileges and the rest of the packet's body are not kept. The
    /// order against <see cref="EntityArrivals"/> is not stable (see there).</remarks>
    IReadOnlyList<ReceivedPlayerData> PlayerData();

    /// <summary>Gets whether the server sent the player a world-data packet for the given player
    /// uid, departures excluded (the receiving player's own uid included).</summary>
    /// <param name="playerUid">The uid to look for (<c>IPlayer.PlayerUID</c>).</param>
    /// <returns><see langword="true"/> when a record for that uid with
    /// <see cref="ReceivedPlayerData.IsDeparture"/> false was captured since the join or the last
    /// clear.</returns>
    bool HasReceivedPlayerData(string playerUid);

    /// <summary>Gets every full player-groups listing (packet 49) the server sent to the
    /// player, oldest first. The engine sends one on every join, so the first listing of a
    /// player is a free positive control, and sends another when the player leaves a group, is
    /// kicked from one, or a group is disbanded.</summary>
    /// <returns>The listings captured since the join or the last clear, not since the last
    /// read.</returns>
    IReadOnlyList<ReceivedGroupListing> GroupListings();

    /// <summary>Gets every single-group update (packet 50) the server sent to the player,
    /// oldest first: a group created, an invitation or an acceptance, a rename. A client only
    /// adds or replaces a group on such a packet, so it never proves a group was dropped: that
    /// is what the next <see cref="GroupListings"/> entry is for.</summary>
    /// <returns>The updates captured since the join or the last clear, not since the last
    /// read.</returns>
    IReadOnlyList<ReceivedGroupUpdate> GroupUpdates();

    /// <summary>Forgets everything captured so far, undecoded packets included, so the next
    /// reads only reflect what the server sends from now on. A <c>Sequence</c> keeps counting
    /// across it, so records read before and after stay in order. A <c>RollbackWorld</c> restore
    /// does this too.</summary>
    void Clear();
}
