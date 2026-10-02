using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>The subset of what the server sent to one test player that Atlas decodes (block
/// highlights, particles, mod-channel packets, chat lines, entity arrivals and departures,
/// player world data and player-group listings): the client-side assertion surface for mods
/// whose server side drives effects on the client without a client process, and for server code
/// that decides what a client may learn. Every other packet is dropped as it arrives, without
/// being decoded.</summary>
/// <remarks><para>A test player's connection receives every packet a real client would; nothing
/// renders it, so the bytes wait in the connection's receive buffer. Atlas empties that buffer
/// on every server pass, with a tick listener per joined test player that takes the packets out:
/// it drops at once, without decoding it, every packet of a kind Atlas does not decode (chunks,
/// entity positions and attributes, and the rest of what a client is sent) and stamps each of the
/// others with the tick it found it in. The decoding, with the engine's own packet serializer, happens when the
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
/// <see cref="EntityArrivals"/>, <see cref="EntityDepartures"/>, <see cref="PlayerData"/>,
/// <see cref="GroupListings"/>, <see cref="GroupUpdates"/>, <see cref="Chat"/> and
/// <see cref="Packets{T}"/> instead.
/// (<see cref="Packets{T}"/> validates its channel and message type before it reads, so a call
/// that throws decodes nothing.)</para>
/// <para><b>Tick and Sequence.</b> Every entity, departure, player-data and group record carries the
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
/// Atlas does not decode are dropped as they arrive, without being decoded, and the UDP queue
/// every test player shares
/// is emptied on every pass (nothing in it is observable; UDP mod channels are not captured). The
/// kinds Atlas does decode accumulate until a read or <see cref="Clear"/>, as the lists above
/// say, so a long scenario that never reads should call <see cref="Clear"/> now and then. The
/// mod-channel packets are the ones to watch: they are kept for every channel, the game's own
/// included, and on vanilla those send about one packet per pass while animated entities are near
/// the player. What that adds up to depends on how many entities are near and how many of them
/// animate, and on the mods that send on a channel of their own, so measure it on your own scene.
/// Measured, one player that never reads: 100 hens near the spawn parked 1,643 packets in 1,800
/// passes on 1.22.3 (1,109 on 1.21.7), 44 bytes each and 72 KB a minute of <see cref="UnreadBytes"/>
/// (40 bytes and 44 KB a minute on 1.21.7); a scene with 123 entities near and a mod channel of its
/// own, 47 bytes each and 77 KB a minute. The join's own packets add about 0.28 MB once, see
/// <see cref="UnreadPackets"/>.</para></remarks>
public interface IClientObservations
{
    /// <summary>Gets how many packets this player holds that no read has decoded yet: the ones of
    /// the kinds Atlas decodes that the per-pass drain parked, waiting for a read or for
    /// <see cref="Clear"/>. Zero right after either.</summary>
    /// <remarks><para>This is the bound a scenario that never reads can assert. A test player holds
    /// the packets of the kinds Atlas decodes (entity arrivals and departures, player data, group
    /// packets, chat lines, highlights, particles and mod-channel packets) from the pass that took
    /// them out of the engine's receive buffer until a read or <see cref="Clear"/>. The packets Atlas
    /// does not decode are dropped as they arrive and never counted. Reading the count does not
    /// drain that buffer, decode anything or allocate, so a packet the server sent since the last
    /// pass is counted from the next pass on. Any read of this interface, <see cref="Clear"/> and a
    /// <c>RollbackWorld</c> restore bring it back to zero. What a read decoded stays in the lists
    /// below until <see cref="Clear"/> and is not counted here.</para>
    /// <para>The counters read 0 right after <c>JoinPlayer</c> returns, and the join's own packets
    /// are counted a pass or two later: the entity list, the player data, the group listing, the
    /// welcome line and the engine's mod-channel packets reach the player after the join returns and
    /// are parked by the passes that follow. Measured in a world without mods, 0 packets right
    /// after the join and after one tick, then 21 to 22 packets and 264 to 277 KB at the second
    /// tick (1.21.7 and 1.22.3); in a scene of Pulse's, 26 packets and 279 KB one tick after, one of
    /// them a single mod-channel packet of 247 KB. So "nothing unread" asserted right after the
    /// join holds for the wrong reason, and a player that never reads holds about 0.28 MB from its
    /// first ticks on.</para>
    /// <para>The number of packets is also the better proxy for the memory held: each parked packet
    /// costs a fixed amount on top of its bytes, see <see cref="UnreadBytes"/>.</para></remarks>
    int UnreadPackets { get; }

    /// <summary>Gets the size, in bytes, of the <see cref="UnreadPackets"/>: the length of each
    /// serialized packet as the server sent it, summed. See <see cref="UnreadPackets"/> for what is
    /// and is not counted.</summary>
    /// <remarks><para>This is the serialized size of the unread packets only, not the memory the
    /// player holds. Each parked packet also costs the engine's message object and its array header
    /// (about 75 bytes in the runs measured for this page, whatever the packet's size) and a 16 byte
    /// slot in the list that holds it, which doubles at powers of two, so the memory is larger than
    /// this number and the gap depends on how small the packets are. Measured on 1.22.3,
    /// <see cref="Clear"/> freed 96 bytes of heap per packet for 19 byte chat lines and 383 for 311
    /// byte ones (4,000 parked packets each); in a scene whose packets were 47 bytes it freed 121
    /// (Pulse's, 3 players or 1).
    /// Those are examples, not a ratio to rely on. When you want a bound on the memory, bound
    /// <see cref="UnreadPackets"/>, which follows the heap more closely when packets are small.</para></remarks>
    long UnreadBytes { get; }

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
    /// record of what did. The behavior of the vanilla engine, measured on 1.20.12 to 1.22.7, is
    /// worth knowing before asserting on it, and a fork can differ (the last item):</para>
    /// <list type="bullet">
    /// <item><description>A vanilla bug in the engine's spawn queue
    /// (<c>PhysicsManager.PrepareEntitySpawns</c>) makes packet 34 reach only the first third of
    /// the connected clients. The others get the same entity later, as packet 33. So
    /// <see cref="ReceivedEntity.Path"/> is a diagnostic: assert on the union of the paths, with
    /// <see cref="HasReceivedEntity"/>.</description></item>
    /// <item><description>An arrival is not instantaneous. It usually comes within about ten passes
    /// (measured: 1 to 6 after a spawn, 0 to 9 after a return into range), but slower rounds
    /// happen: 29 to 38 passes was measured in 3 rounds out of 20 on one run, and 31 earlier with
    /// several observers. There is no upper bound to count on. An absence assertion waits a
    /// generous window (60 passes has covered every measurement so far, as a margin and not as a
    /// bound) and needs a positive control, an arrival in the same window that proves the observer
    /// was listening.</description></item>
    /// <item><description>Duplicates are real: an entity can arrive as 33 and 34 in the same
    /// pass, and on vanilla a player's own entity arrives twice, as 40 at the join and as 33 a few
    /// passes later.</description></item>
    /// <item><description>Packet 40 depends on the version. On vanilla 1.22.x it carries only the
    /// joining player's own entity. On 1.21.x and 1.20.x it also carries the entities of the
    /// players already connected to the joiner, and re-sends a connected player's entity to the
    /// others when somebody joins.</description></item>
    /// <item><description>The order between a player's <see cref="PlayerData"/> record and its
    /// entity differs by path: player data comes first on 33 and after the entity on 34. Do not
    /// depend on it.</description></item>
    /// <item><description><c>JoinPlayer</c> returns a few passes after the joiner's entity reached
    /// the other clients (5 to 6 measured), so a wait counted from the return of
    /// <c>JoinPlayer</c> overcounts the passes since the arrival by that much.</description></item>
    /// <item><description>After a <c>RollbackWorld</c> restore the stores are empty and the
    /// server does not send the entities again, so "never received" checked right after a
    /// restore holds for the wrong reason.</description></item>
    /// <item><description>A fork can send other paths than the vanilla ones above. One that batches
    /// the entities entering a client's range (Stratum's) sends them through packet 40, never
    /// through 33. What such a fork shows, measured on 1.22.7 with three players: packet 33 never
    /// appears and <see cref="EntityArrivalPath.JoinList"/> is the path of every entity that entered
    /// the range; a player's own entity arrives twice, both times through packet 40; and one packet
    /// 40 carries every connected player's entity, not only the joining player's own. The union of
    /// the paths is still what to assert on.</description></item>
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
    /// <remarks>This is the member to assert an arrival with. It stays <see langword="true"/> after
    /// the entity departs, and reads <see langword="false"/> after a clear for an entity the client
    /// still holds: ask <see cref="KnowsEntity"/> whether the client holds it now. An absence
    /// assertion is only meaningful after a generous wait since the entity spawned or came into
    /// range, with a positive control that proves the observer was listening (see
    /// <see cref="EntityArrivals"/> for how long an arrival can take), and not right after a
    /// rollback. The recipe is on the Client-Side Testing wiki page.</remarks>
    bool HasReceivedEntity(long entityId);

    /// <summary>Gets every entity the server told the player is gone (packet 36), oldest first:
    /// one record per entity entry of every despawn packet, duplicates kept.</summary>
    /// <returns>The departures captured since the join or the last clear, not since the last
    /// read.</returns>
    /// <remarks><para>A client is told an entity is gone by packet 36. The engine sends it when an
    /// entity despawns on the server and when the server stops tracking an entity for one client,
    /// and a fork can send it to hide an entity from one client in the middle of a session (that is
    /// how such a hide shows up here). Measured on 1.21.7 and 1.22.3:</para>
    /// <list type="bullet">
    /// <item><description>A despawn is reported to every client that tracks the entity, usually
    /// twice: once from the despawn queue, 1 to 4 passes after the despawn, then once more as
    /// <see cref="EnumDespawnReason.OutOfRange"/>, up to 7 passes after, when the tracking pass
    /// notices the entity is gone. Sometimes only the second one comes. The first one's reason is the
    /// entity's own despawn reason, which is <see cref="EnumDespawnReason.Death"/> when it has none,
    /// so a despawn asked for with another reason can read as <c>Death</c>. Assert on the entity and
    /// on <see cref="KnowsEntity"/>, not on the number of records or on the reason, unless you
    /// measured them on the build you test.</description></item>
    /// <item><description>An entity that moves out of the client's range, a player teleported far
    /// away for instance, is reported as <c>OutOfRange</c> in the same pass, and arrives again the way
    /// any entity entering the range does (see <see cref="EntityArrivals"/> for how long
    /// that can take).</description></item>
    /// <item><description>A client that moves away from entities is not told they are gone, from
    /// about 150 blocks on (measured: reported at 140 blocks, not at 170 and beyond). The server
    /// unloads that client's chunks instead, with a packet Atlas does not decode, and a real client
    /// drops the entities that were in them. <see cref="KnowsEntity"/> keeps answering
    /// <see langword="true"/> for them: move the entity, not the observer, when a scenario needs a
    /// departure.</description></item>
    /// <item><description>A player who disconnects departs for the clients that track his entity,
    /// and every client also gets the player-data departure of <see cref="PlayerData"/>, which says
    /// the player left and not that an entity is gone.</description></item>
    /// <item><description>A dimension change alone is not a departure: the engine tracks an entity
    /// by its coordinates, whatever its dimension. A transit that also moves the entity far away
    /// from where a witness stands (a dimension whose arrival point is hundreds of thousands of
    /// blocks off) is one: the witness gets a departure with reason
    /// <see cref="EnumDespawnReason.OutOfRange"/>, then a new arrival when the entity comes
    /// back.</description></item>
    /// </list>
    /// <para>Do not pair a departure with an arrival. An entity can depart that has no arrival in
    /// <see cref="EntityArrivals"/> because the arrival predates the last <see cref="Clear"/>. After
    /// a <c>RollbackWorld</c> restore that is the usual case: the restore empties the stores, and the
    /// server then despawns the entities the restore removed, a few passes later. Ask
    /// <see cref="KnowsEntity"/> whether the client holds an entity now.</para>
    /// <para>A departure is not instantaneous: an assertion that one happened waits for it, and an
    /// assertion that none did needs a window and a positive control, like an absence of arrivals.</para>
    /// <para>Packet 36 comes from two engine senders. The despawn queue is flushed by the engine's
    /// 100 ms update whenever it holds something, and every connected client then gets one packet,
    /// whatever the despawns were: it lists the queued entities that client tracks, so it has no
    /// ids (6 bytes, and no record here) for a client that tracked none of them, and 6 bytes of
    /// header plus about 6 per id for one that did (12 bytes for one entity, 30 for four, measured
    /// on 1.21.7 and 1.22.3; an id is a varint, so a large one costs more). Despawns that fall in
    /// the same flush share one packet. The tracking pass is the second sender: it tells a client
    /// that tracked an entity it no longer finds in range, with reason
    /// <see cref="EnumDespawnReason.OutOfRange"/>, usually a few passes later and never empty. So
    /// one despawn pass typically parks two packets with ids for a client that tracked the entity,
    /// and one empty packet for a client that did not. A despawn anywhere in the world flushes a
    /// packet to every client, so a client that tracked the entity can also be sent an empty packet
    /// for a despawn it did not track (seen on 1.21.7).</para></remarks>
    IReadOnlyList<ReceivedEntityDeparture> EntityDepartures();

    /// <summary>Gets whether the client currently knows the entity with the given id: the server
    /// sent it to the player, over any path of <see cref="EntityArrivalPath"/>, and has not told the
    /// player it is gone since. The client's present state, which is not the question
    /// <see cref="HasReceivedEntity"/> answers (whether an arrival was captured since the last
    /// clear).</summary>
    /// <param name="entityId">The entity's id (<c>Entity.EntityId</c>).</param>
    /// <returns><see langword="true"/> when the last thing the server told the player about that
    /// entity, in the order the player received it, was an arrival.</returns>
    /// <remarks><para>It is derived from the packets, in the order the player received them: an
    /// arrival over any of the three paths adds the entity and a departure removes it, so an entity
    /// that arrived, departed and arrived again is known. It starts at the join. Unlike every list
    /// of this interface it belongs to no capture window: <see cref="Clear"/> and a
    /// <c>RollbackWorld</c> restore leave it as it is, after applying the arrivals and departures
    /// they drop. A <see cref="Clear"/> before the event under test therefore cannot turn "the
    /// client knows the entity" into <see langword="false"/> for the wrong reason, which is what
    /// <see cref="HasReceivedEntity"/> does after a clear, and that member stays
    /// <see langword="true"/> after a departure.</para>
    /// <para>As for arrivals, an assertion that the client does not know an entity needs a wait (a
    /// departure takes a pass or more) and a positive control, an entity the observer was told about
    /// in the same window. It only follows what the server tells the client about an entity: a
    /// client that moves far away from one is not told, and keeps it here (see
    /// <see cref="EntityDepartures"/>).</para></remarks>
    bool KnowsEntity(long entityId);

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
