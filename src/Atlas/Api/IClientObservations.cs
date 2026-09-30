namespace Atlas.Api;

/// <summary>What the server sent to one test player, decoded as a client would decode it: the
/// client-side assertion surface for mods whose server side drives effects on the client
/// (block highlights, particles, mod-channel packets, chat lines) without a client process.</summary>
/// <remarks><para>A test player's connection receives every packet a real client would; nothing
/// renders it, so the bytes wait in the connection's receive buffer. Each read on this surface
/// first drains and decodes whatever arrived since the previous read, with the engine's own
/// packet serializer, then answers. Every member runs on the game thread, like the rest of
/// <see cref="ITestPlayer"/>; the packets themselves are captured synchronously by the send,
/// so a server call followed by a read on the same tick observes the packet (no ticks needed)
/// as long as the engine actually sends it: particles, for instance, only go to players whose
/// chunk at the spawn position was already streamed.</para>
/// <para>That drain is exclusive. Every member (<see cref="Highlights"/>, <see cref="Particles"/>,
/// <see cref="Packets{T}"/>, <see cref="Chat"/>, <see cref="ChatLines"/> and <see cref="Clear"/>)
/// dequeues everything pending on the test player's dummy connection
/// (<c>DummyTcpNetClient.ReadMessage</c>) and keeps only the highlight, particle, mod-channel and
/// chat messages; every other server packet (entity spawns, player groups, ...) is decoded and
/// dropped. So nothing else can read that buffer once <c>player.Client</c> has been read: a
/// helper that inspects the same buffer by reflection sees nothing of what arrived before that
/// read, and an assertion there that a packet is absent passes for the wrong reason. Only a
/// positive control, a case where the packet is known to arrive, tells the two apart.
/// (<see cref="Packets{T}"/> validates its channel and message type before it reads, so a call
/// that throws drains nothing.)</para>
/// <para>Observations accumulate for the player's lifetime, from the join, and a read does not
/// consume them: <see cref="Chat"/>, <see cref="ChatLines"/>, <see cref="Particles"/> and
/// <see cref="Packets{T}"/> return everything captured since the join or the last
/// <see cref="Clear"/>, not since the last read (a second read returns the first one's lines
/// plus whatever arrived in between), and <see cref="Highlights"/> keeps each slot's latest state
/// the same way. A scenario that wants a window calls <see cref="Clear"/> first. They are also
/// cleared by a <c>RollbackWorld</c> restore, which does what <see cref="Clear"/> does (the world
/// state rewound, stale observations would mislead). A <c>FreshWorld</c> recycle joins new
/// players on a new host, so nothing carries over there either. Only the TCP stream is captured:
/// packets sent over a UDP mod channel (<c>RegisterUdpChannel</c>) are not observed.</para></remarks>
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

    /// <summary>Forgets everything captured so far, undecoded packets included, so the next
    /// reads only reflect what the server sends from now on. It empties the connection's receive
    /// buffer the way a read does, so a raw reader attached to that buffer before the call loses
    /// whatever was pending in it.</summary>
    void Clear();
}
