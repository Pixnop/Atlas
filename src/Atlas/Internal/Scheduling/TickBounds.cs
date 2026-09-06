namespace Atlas.Internal.Scheduling;

/// <summary>The two tick bounds Atlas's own waits share, named once so the sites that must move
/// together are found by a reference search instead of by reading prose. One tick is one fire of
/// the engine's game-tick listener, at most one per <c>ServerMain.Process()</c> pass, about 33 ms
/// at the engine's default pacing (<c>ServerConfig.TickTime</c>).</summary>
/// <remarks>Bounds that belong to one wait only are not here: they live as a const next to the
/// wait that uses them, where their rationale is (the assets-build settle bound in
/// <c>WorldSession</c>, the rollback bounds in <c>WorldSnapshot</c>, the bridge startup passes in
/// <c>ServerHost</c>).</remarks>
internal static class TickBounds
{
    /// <summary>The default bound on an open-ended conditional wait: about 20 seconds, generous
    /// enough for a chunk load or a save to finish on a loaded CI runner, short enough that a
    /// scenario waiting on something that will never happen fails before the outer job timeout.
    /// This is the documented default of <c>IWorldSession.Until</c> and the bound Atlas's own
    /// waits of that shape use (a teleport's deferred application, the release of a removed
    /// player's name claims).</summary>
    internal const int DefaultWait = 600;

    /// <summary>The bound on a wait for the engine's own machinery to complete a handshake it
    /// has already been asked for: about 3 seconds, an order of magnitude over what these take
    /// when they work (a client join registering, a client reaching Playing, a sent packet being
    /// parsed off the wire). Deliberately shorter than <see cref="DefaultWait"/>: the engine
    /// either answers promptly or has drifted, and the diagnosis is worth more than the
    /// wait.</summary>
    internal const int EngineHandshake = 100;
}
