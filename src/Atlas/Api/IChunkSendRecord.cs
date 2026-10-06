namespace Atlas.Api;

/// <summary>The server's own record of the chunks and map chunks it has sent one test player: the
/// per-client bookkeeping the engine keeps to decide what to send next and what to unload.</summary>
/// <remarks><para>This is server data filtered by what the server believes it sent, not a view of
/// the client. A scenario reads blocks and heights from the live world and uses this record to say
/// which of them the player was sent: a chunk the record lacks was not sent (yet, or any more), so a
/// check that reads the world only where the client would hold the chunk can mirror a client that
/// has not received it. The record does not hold or decode what was sent, so it cannot say what a
/// chunk looked like when it went. A record entry is made in the pass that sends the packet, and a
/// scenario reads between passes, so the record and the packets in the player's connection agree
/// whenever a scenario looks.</para>
/// <para>The members read the engine's <c>ConnectedClient.ChunkSent</c> and
/// <c>ConnectedClient.MapChunkSent</c>. Both are plain hash sets that the engine changes on the game
/// thread, in its send and unload passes, so every member runs on the game thread, like the rest of
/// <see cref="ITestPlayer"/>, and is not safe to call from another one. A set says whether a chunk
/// is in it, not how often it went out: a chunk sent again while its entry is still there is not
/// observable as a second send, and nothing in the record counts sends or keeps their order.</para>
/// <para>Entries go away when the engine unloads a chunk for the player: when the player moves out
/// of range of a column in the overworld, and when the server unloads the chunk. The slices of a
/// dimension other than the overworld are never unloaded for a player, so once recorded they stay
/// recorded. A class that runs a <c>RollbackWorld</c> scenario captures a baseline, and the capture
/// turns the engine's chunk unloading off for the rest of the class (<c>/chunk unload false</c>):
/// no entry goes away from then on, and no unload packet is sent. A player that is gone (kicked
/// or left) keeps its record as it was: nothing updates it afterwards.</para></remarks>
public interface IChunkSendRecord
{
    /// <summary>Tells whether the server has sent this player the chunk at the given chunk
    /// coordinates and has not unloaded it for the player since.</summary>
    /// <param name="cx">The chunk's x: a block x divided by 32, rounded down.</param>
    /// <param name="cy">The chunk's y, with the dimension folded in as
    /// <c>IWorldManagerAPI.HasChunk</c> does: a block y divided by 32, rounded down, plus 1024
    /// times the dimension. The slices of the overworld are 0 up to the world height divided by
    /// 32, those of dimension 1 start at 1024.</param>
    /// <param name="cz">The chunk's z: a block z divided by 32, rounded down.</param>
    /// <returns><see langword="true"/> when the chunk is in the record. <see langword="false"/>
    /// for a chunk never sent, one unloaded for the player since, and a position outside the
    /// world (a negative coordinate, or an overworld column past the world's edge).</returns>
    /// <remarks>Runs on the game thread. It answers what <c>IWorldManagerAPI.HasChunk</c> answers
    /// for this player, with one difference: the engine's index arithmetic folds a position
    /// outside the world onto another chunk, so <c>HasChunk</c> can say <see langword="true"/> for
    /// it where this says <see langword="false"/>.</remarks>
    bool WasSentChunk(int cx, int cy, int cz);

    /// <summary>Tells whether the server has sent this player the map chunk of a column (the
    /// per-column data next to its slices, such as the height maps) and has not unloaded it for the
    /// player since.</summary>
    /// <param name="cx">The column's chunk x.</param>
    /// <param name="cz">The column's chunk z.</param>
    /// <returns><see langword="true"/> when the map chunk is in the record.
    /// <see langword="false"/> for a map chunk never recorded, one dropped since, and a column
    /// outside the world.</returns>
    /// <remarks><para>Runs on the game thread. A map chunk belongs to an (x, z) column of the
    /// overworld, not to a dimension: there is one record entry per column whatever dimension the
    /// slices sent are in, and the answer is keyed by that overworld column. A dimension column
    /// that the overworld's own streaming already served therefore reads
    /// <see langword="true"/>, whatever the dimension did. A test of a dimension's map chunks needs
    /// a column the overworld never streamed to this player, so it has to look far from where the
    /// player has been (the Manifold suite moved 512 blocks away).</para>
    /// <para>Only the engine's own streaming of chunks around the player makes this entry, which
    /// queues a column's map chunk with the first slice it sends. A map chunk that goes out by a
    /// forced send (<c>IWorldManagerAPI.ResendMapChunk</c>, or the map chunk that
    /// <c>SendChunk</c> and <c>BroadcastChunk</c> add) is sent without making an entry, and
    /// <c>ForceSendChunkColumn</c> sends a column's slices and no map chunk at all. So
    /// <see langword="false"/> means the streaming did not send it, not that the player holds
    /// none. The entry is dropped with a chunk the server unloads, even when other slices of the
    /// column stay recorded.</para></remarks>
    bool WasSentMapChunk(int cx, int cz);
}
