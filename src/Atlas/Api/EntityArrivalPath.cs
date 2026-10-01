namespace Atlas.Api;

/// <summary>The wire path a <see cref="ReceivedEntity"/> reached a test player by. The numeric
/// value is the engine's own packet id on every supported version (1.20.12 to 1.22.7).</summary>
/// <remarks>This is a diagnostic, not something to assert on. Which path carries an entity to
/// which client is the engine's business and differs between versions and between clients of
/// the same server: assert on the union of the three paths
/// (<see cref="IClientObservations.HasReceivedEntity"/>), never on one of them. The measured
/// path map is on the Client-Side Testing wiki page.</remarks>
public enum EntityArrivalPath
{
    /// <summary>Packet 33, one entity: an existing entity entering the client's tracked range,
    /// including a player returning into range and the observer's own entity. It arrives 1 to 31
    /// passes after the spawn or the return, not in the pass the entity became visible.</summary>
    TrackedRange = 33,

    /// <summary>Packet 34, a batch of fresh spawns queued per client. A vanilla bug in the
    /// engine's spawn queue makes it reach only the first third of the connected clients, so a
    /// client missing it is normal: the same entity then arrives as <see cref="TrackedRange"/>.</summary>
    Spawn = 34,

    /// <summary>Packet 40, a list of entities sent around a join. On 1.22.x it carries only the
    /// joining player's own entity to that player. On 1.21.x and 1.20.x it also carries the
    /// entities of the players already connected to the joining one, and re-sends an existing
    /// player's entity to the others when somebody joins.</summary>
    JoinList = 40,
}
