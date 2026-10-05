using Atlas.Api;
using Vintagestory.Server;

namespace Atlas.Internal.Player;

/// <summary>Answers <see cref="IChunkSendRecord"/> from the engine's own per-client bookkeeping,
/// <c>ConnectedClient.ChunkSent</c> and <c>ConnectedClient.MapChunkSent</c>.</summary>
/// <remarks>Both are public fields of a public class on 1.21.7, 1.22.3 and 1.22.7, filled and
/// emptied only by the engine's send and unload systems, which tick on the game thread: the
/// compiled reference needs no probe, and a read from the game thread never races a write. The
/// engine's own <c>DidSendChunk</c> and <c>DidSendMapChunk</c> do the lookups, with the indices
/// <c>ServerMain.WorldMap</c> gives (the same ones <c>IWorldManagerAPI.HasChunk</c> uses), so a
/// change to the key layout cannot leave this behind. The position is checked first because those
/// indices fold a position outside the world onto another chunk. The chunk index is asked through
/// the dimension-aware overload with dimension 0: the three-argument one is marked obsolete on
/// 1.21.7, where the build treats a warning as an error, and the two give the same index (the
/// caller's <c>cy</c> already carries the dimension).</remarks>
internal sealed class ChunkSendRecord : IChunkSendRecord
{
    private readonly ServerMain _server;
    private readonly ConnectedClient _client;

    /// <summary>Initializes a new instance of the <see cref="ChunkSendRecord"/> class.</summary>
    /// <param name="server">The live server, for its chunk index arithmetic.</param>
    /// <param name="client">The connected client whose record this reads.</param>
    public ChunkSendRecord(ServerMain server, ConnectedClient client)
    {
        _server = server;
        _client = client;
    }

    /// <inheritdoc/>
    public bool WasSentChunk(int cx, int cy, int cz)
        => _server.WorldMap.IsValidChunkPos(cx, cy, cz)
            && _client.DidSendChunk(_server.WorldMap.ChunkIndex3D(cx, cy, cz, 0));

    /// <inheritdoc/>
    public bool WasSentMapChunk(int cx, int cz)
        => _server.WorldMap.IsValidChunkPos(cx, 0, cz)
            && _client.DidSendMapChunk(_server.WorldMap.MapChunkIndex2D(cx, cz));
}
