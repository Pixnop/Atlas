namespace Atlas.Internal.Player;

/// <summary>Reads <c>Packet_Server.Id</c> off a serialized server packet without deserializing
/// it, and says which ids <see cref="ClientObservations"/> decodes: the filter that lets it drop
/// everything else the moment it is dequeued.</summary>
/// <remarks><para>Where the id sits (verified by decompile on 1.21.7, 1.22.3 and 1.22.7, where
/// <c>Packet_ServerSerializer.Serialize</c> is the same up to a field Atlas does not read): every
/// <c>Packet_Server</c> that reaches a client goes through that serializer
/// (<c>BoxedPacket.Serialize</c>, <c>Packet_Server.SerializeTo</c>), the dummy connection
/// enqueues those bytes as they are (no length prefix, never compressed for a singleplayer-type
/// client), and the serializer writes <c>Id</c> first, as field 90 with wire type 0:
/// the key is the varint 720, the two bytes <c>D0 05</c>, followed by the id as a varint. It
/// omits the field only when the id is its default, 1, the server identification, which is why a
/// message that does not start with the key has no readable id.</para>
/// <para>What is kept: <see cref="IsDecoded"/> lists the eleven ids whose sub-message
/// <c>ClientObservations.Apply</c> reads. The engine pairs each one with that sub-message alone
/// at every send site (decompiled on the same three versions, and its own client dispatches on
/// the id), so keeping by id keeps exactly what the sub-message dispatch would have decoded.
/// Only decoded kinds are parked, the rest is dropped as it arrives: that is what bounds what a
/// scenario that never reads holds.</para>
/// <para>The rule never guesses. A message whose id cannot be read, because it does not start
/// with the key (the identification packet, once per join) or is cut short, is parked like a
/// decoded kind, so the read that meets it decodes it and reports a failure as before.</para></remarks>
internal static class ServerPacketId
{
    /// <summary>The first byte of the id field's key (720 as a varint: low seven bits, with the
    /// continuation bit).</summary>
    private const byte KeyFirstByte = 0xD0;

    /// <summary>The second byte of the id field's key (720 as a varint: the remaining bits).</summary>
    private const byte KeySecondByte = 0x05;

    /// <summary>The longest varint the engine's reader accepts for a 32-bit value.</summary>
    private const int MaxVarintBytes = 5;

    /// <summary>Reads the id of a serialized <c>Packet_Server</c>.</summary>
    /// <param name="buffer">The message bytes.</param>
    /// <param name="length">The number of valid bytes in <paramref name="buffer"/>; the buffer
    /// may be longer.</param>
    /// <param name="id">The packet id when it could be read, otherwise 0.</param>
    /// <returns>Whether the message starts with the id field and the id was read.</returns>
    public static bool TryRead(byte[] buffer, int length, out int id)
    {
        id = 0;
        if (length < 3 || length > buffer.Length || buffer[0] != KeyFirstByte || buffer[1] != KeySecondByte)
        {
            return false;
        }

        int value = 0;
        for (int i = 0; i < MaxVarintBytes && i + 2 < length; i++)
        {
            byte next = buffer[i + 2];
            value |= (next & 0x7F) << (7 * i);
            if ((next & 0x80) == 0)
            {
                id = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether a packet id is one of the kinds <see cref="ClientObservations"/>
    /// decodes.</summary>
    /// <param name="id">The packet id.</param>
    /// <returns>Whether the id is one of the eleven decoded kinds.</returns>
    public static bool IsDecoded(int id) => id is
        8 // chat line
        or 33 // entity, tracked range
        or 34 // entity spawn
        or 36 // entity despawn
        or 40 // entity list, join
        or 41 // player world data
        or 49 // player groups listing
        or 50 // player group update
        or 52 // block highlight
        or 55 // mod channel custom packet
        or 61; // particles

    /// <summary>Tells whether a packet id is one of the kinds that changes which entities a client
    /// holds: an arrival (33, 34, 40) or a despawn (36).</summary>
    /// <param name="id">The packet id.</param>
    /// <returns>Whether the id is one of the four entity-presence kinds.</returns>
    public static bool ChangesEntityPresence(int id) => id is 33 or 34 or 36 or 40;

    /// <summary>Tells whether a dequeued message is worth parking: its id is one of the decoded
    /// kinds, or it cannot be read at all (see the class remarks).</summary>
    /// <param name="buffer">The message bytes.</param>
    /// <param name="length">The number of valid bytes in <paramref name="buffer"/>.</param>
    /// <returns><see langword="false"/> only for a message whose id was read and is not a decoded
    /// kind.</returns>
    public static bool ShouldPark(byte[] buffer, int length) => !TryRead(buffer, length, out int id) || IsDecoded(id);
}
