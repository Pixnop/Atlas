using Atlas.Internal.Hosting;

namespace Atlas.XUnit.Internal;

/// <summary>The outcome of one completed <see cref="HostRegistry.RestartAsync"/> call: the
/// replacement host the scenario must run on, plus the measured wall-clock cost of the restart
/// (shutdown + harvest + boot). A restart never degrades (it works or fails the scenario hard),
/// so unlike <see cref="RollbackOutcome"/> there is no failure evidence to carry; the cost is
/// the whole story, and the invoker turns it into the scenario's isolation report because the
/// restart runs before the timed body and would otherwise be invisible.</summary>
/// <param name="Host">The replacement host, booted against the outgoing host's persisted save.</param>
/// <param name="Cost">Wall-clock cost of the restart (shutdown + harvest + boot).</param>
internal sealed record RestartOutcome(ServerHost Host, TimeSpan Cost);
