using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One player world-data packet (packet 41) the server sent to a test player: the
/// identity block a client receives for another player, for itself, and when a player leaves.</summary>
/// <param name="PlayerUid">The player's uid. Never null: empty when the packet carried none.</param>
/// <param name="PlayerName">The player's name. Empty on the first, partial packet a joining
/// player gets about itself.</param>
/// <param name="EntityId">The id of the player's entity, <c>0</c> on a departure.</param>
/// <param name="ClientId">The server-side client id, or <c>-99</c> on a departure.</param>
/// <param name="GameMode">The player's game mode as the packet carried it. A joining player
/// receives two packets about itself with different modes on a vanilla server, so assert on the
/// presence of the uid, not on a game mode history.</param>
/// <param name="IsDeparture">Whether this is the engine's "player left" broadcast, which it
/// marks with client id <c>-99</c> (<paramref name="ClientId"/>).</param>
/// <param name="IsSelf">Whether <paramref name="PlayerUid"/> is the receiving test player's own
/// uid: a client also receives packets about itself, and nothing in the packet's shape tells
/// them apart from the ones about others.</param>
/// <param name="Tick">The arrival tick, with the meaning of <see cref="ReceivedEntity.Tick"/>.</param>
/// <param name="Sequence">The arrival order, with the meaning of <see cref="ReceivedEntity.Sequence"/>.</param>
public sealed record ReceivedPlayerData(
    string PlayerUid,
    string PlayerName,
    long EntityId,
    int ClientId,
    EnumGameMode GameMode,
    bool IsDeparture,
    bool IsSelf,
    int Tick,
    int Sequence);
