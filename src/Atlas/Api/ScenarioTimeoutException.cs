namespace Atlas.Api;

/// <summary>Thrown when a scenario wait exceeds its tick bound, or when the whole scenario
/// exceeds its watchdog.</summary>
/// <remarks>The public throwers are <see cref="IWorldSession.Until"/> (its
/// <c>timeoutTicks</c> elapsed with the predicate still false), <see cref="ITestPlayer.TeleportTo"/>
/// and <see cref="ITestPlayer.Say"/> (their own internal bounds), <see cref="IWorldSession.JoinPlayer"/>
/// (the joining player's inventories were not wired up within 100 ticks of the RequestJoin
/// packet), and the per-scenario watchdog (<c>AtlasScenario.TimeoutMs</c>, 60 seconds by
/// default). <see cref="IWorldSession.Ticks"/> never throws it: a fixed tick wait has nothing
/// to time out on.</remarks>
public sealed class ScenarioTimeoutException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScenarioTimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="ticksWaited">The number of ticks that elapsed.</param>
    public ScenarioTimeoutException(string message, int ticksWaited)
        : base(message)
        => TicksWaited = ticksWaited;

    /// <summary>Gets the number of ticks that elapsed before giving up: the wait's own elapsed
    /// count for a tick-bounded wait, and the host's tick count since boot for a watchdog
    /// timeout, where the scenario's own start tick is not known to the watchdog.</summary>
    public int TicksWaited { get; }
}
