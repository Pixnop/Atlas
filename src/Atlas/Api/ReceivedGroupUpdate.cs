namespace Atlas.Api;

/// <summary>A single player-group update the server sent to a test player (packet 50): one group
/// created, renamed, or the subject of an invitation or an acceptance. Unlike a
/// <see cref="ReceivedGroupListing"/> it only adds or replaces that group on a client, never
/// removes one.</summary>
/// <param name="Tick">The arrival tick, with the meaning of <see cref="ReceivedEntity.Tick"/>.</param>
/// <param name="Sequence">The arrival order, with the meaning of <see cref="ReceivedEntity.Sequence"/>.</param>
/// <param name="Group">The group the packet described.</param>
public sealed record ReceivedGroupUpdate(int Tick, int Sequence, ReceivedPlayerGroup Group);
