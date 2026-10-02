using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One entity the server told a test player is gone, decoded from the packet that
/// carries a despawn (packet 36): which entity, why, and when.</summary>
/// <param name="EntityId">The entity's id, equal to <c>Entity.EntityId</c> on the server. It
/// need not match an entry of <see cref="IClientObservations.EntityArrivals"/>: the arrival may
/// predate the last <see cref="IClientObservations.Clear"/>, which a rollback restore also
/// does.</param>
/// <param name="Reason">The engine's reason for the despawn, or <see langword="null"/> when the
/// packet carried none for this entity (vanilla always does). A despawn that comes from the
/// server's tracked-range pass, the entity leaving the observer's range or the observer's chunk
/// not being streamed, reads <see cref="EnumDespawnReason.OutOfRange"/>. A value the enum does
/// not name is kept as the number the packet carried.</param>
/// <param name="Tick">The value of <see cref="IWorldSession.CurrentTick"/> during the server pass
/// whose drain found the packet in the player's receive buffer, or at the read when a read found
/// it first. An arrival stamp, exact to one pass: see <see cref="IClientObservations"/> for the
/// exact rule.</param>
/// <param name="Sequence">The packet's position in the order the player received everything,
/// across every kind of packet, starting at 0 at the join and never reset by
/// <see cref="IClientObservations.Clear"/>. Order records by this, not by <paramref name="Tick"/>.
/// The entities of one packet share its Sequence and are listed in the packet's own order, and
/// the same sequence numbers the arrivals, so an arrival and a departure of one entity order
/// against each other.</param>
public sealed record ReceivedEntityDeparture(long EntityId, EnumDespawnReason? Reason, int Tick, int Sequence);
