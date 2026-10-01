namespace Atlas.Api;

/// <summary>A full player-groups listing the server sent to a test player (packet 49): every
/// group the player belongs to or is invited to, at that moment. A client replaces what it
/// knows with the list, so a group missing from a later listing is a group the player left.</summary>
/// <param name="Tick">The arrival tick, with the meaning of <see cref="ReceivedEntity.Tick"/>.</param>
/// <param name="Sequence">The arrival order, with the meaning of <see cref="ReceivedEntity.Sequence"/>.</param>
/// <param name="Groups">The groups in the order the server listed them. Empty for a player in no
/// group, which is also what every join starts with: a listing is always sent on join, so an
/// empty first listing is the positive control for the others.</param>
public sealed record ReceivedGroupListing(int Tick, int Sequence, IReadOnlyList<ReceivedPlayerGroup> Groups);
