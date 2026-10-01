namespace Atlas.Api;

/// <summary>One entity the server sent to a test player, decoded from the packet's entity
/// header: which entity, of which type, by which path, and when.</summary>
/// <param name="EntityId">The entity's id, equal to <c>Entity.EntityId</c> on the server.</param>
/// <param name="EntityType">The entity type's code in its short form, the way the server sends
/// it and the way <c>Entity.Code.ToShortString()</c> reads: <c>"chicken-rooster"</c>,
/// <c>"player"</c>, with the <c>game:</c> domain left out and any other domain kept
/// (<c>"mymod:thing"</c>). A <c>game:</c> prefix on the wire is dropped too, so the value does
/// not depend on which form the sender used.</param>
/// <param name="Path">The wire path that carried the entity. A diagnostic only: see
/// <see cref="EntityArrivalPath"/> for why an assertion should use the union of the paths.</param>
/// <param name="Tick">The value of <see cref="IWorldSession.CurrentTick"/> during the server pass
/// whose drain found the packet in the player's receive buffer, or at the read when a read found
/// it first. An arrival stamp, exact to one pass: a packet sent at tick T carries T or T + 1. See
/// <see cref="IClientObservations"/> for the exact rule.</param>
/// <param name="Sequence">The packet's position in the order the player received everything,
/// across every kind of packet, starting at 0 at the join and never reset by
/// <see cref="IClientObservations.Clear"/>. Order records by this, not by <paramref name="Tick"/>,
/// which is shared by every packet of a pass. The entities of one packet share its Sequence and
/// are listed in the packet's own order.</param>
public sealed record ReceivedEntity(long EntityId, string EntityType, EntityArrivalPath Path, int Tick, int Sequence);
