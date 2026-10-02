namespace Atlas.Api;

/// <summary>The wire path a <see cref="ReceivedEntity"/> reached a test player by. The numeric
/// value is the engine's own packet id on every supported version (1.20.12 to 1.22.7).</summary>
/// <remarks>This is a diagnostic, not something to assert on. Which path carries an entity to
/// which client is the engine's business and differs between versions and between clients of
/// the same server: assert on the union of the three paths
/// (<see cref="IClientObservations.HasReceivedEntity"/>), never on one of them. The members
/// describe the vanilla engine; a fork can send an entity by another path than vanilla does (see
/// <see cref="JoinList"/>). The measured path map is on the Client-Side Testing wiki page.</remarks>
public enum EntityArrivalPath
{
    /// <summary>Packet 33, one entity: on vanilla, an existing entity entering the client's
    /// tracked range, including a player returning into range and the observer's own entity. It
    /// arrives some passes after the spawn or the return, usually within about ten and sometimes
    /// many more (see <see cref="IClientObservations.EntityArrivals"/> for the measurements). A
    /// fork that sends the entities entering a range as a list never produces it.</summary>
    TrackedRange = 33,

    /// <summary>Packet 34, a batch of fresh spawns queued per client. A vanilla bug in the
    /// engine's spawn queue makes it reach only the first third of the connected clients, so a
    /// client missing it is normal: the same entity then arrives as <see cref="TrackedRange"/>.</summary>
    Spawn = 34,

    /// <summary>Packet 40, a list of entities. On vanilla it is sent around a join: on 1.22.x it
    /// carries only the joining player's own entity to that player, and on 1.21.x and 1.20.x it
    /// also carries the entities of the players already connected to the joining one, and re-sends
    /// an existing player's entity to the others when somebody joins. A fork can use it for more
    /// (Stratum batches the entities entering a client's range into one packet 40 instead of
    /// packet 33): then it is also the path of those entities, a player's own entity arrives
    /// through it twice, and one list carries every connected player's entity, so read it as "arrived
    /// in a list packet" and keep asserting on the union. The member is not renamed: its value, 40,
    /// is the contract.</summary>
    JoinList = 40,
}
