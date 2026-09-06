using Atlas.Internal.Hosting;
using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>The outcome of one <see cref="HostRegistry.RollbackOrRecycleAsync"/> call: the host
/// the scenario must run on, plus the <see cref="DegradeEvidence"/> when the rollback request
/// had to degrade to a full host recycle and the measured cost of that fallback. The invoker
/// turns a degraded outcome into the scenario's isolation report (and, under strict isolation,
/// into a failure).</summary>
/// <param name="Host">The host the scenario runs on: the rolled-back class host, or the freshly
/// booted replacement when the request degraded.</param>
/// <param name="Degrade">Why the rollback request fell back to a full host recycle, or
/// <see langword="null"/> when it succeeded.</param>
/// <param name="RecycleCost">Wall-clock cost of the fallback recycle (dispose + boot), or
/// <see cref="TimeSpan.Zero"/> when the rollback succeeded.</param>
internal sealed record RollbackOutcome(
    ServerHost Host,
    DegradeEvidence? Degrade,
    TimeSpan RecycleCost)
{
    /// <summary>Gets a value indicating whether the rollback request fell back to a full host
    /// recycle, which is exactly "there is degrade evidence".</summary>
    public bool Degraded => Degrade is not null;

    /// <summary>Creates the outcome of a successful rollback.</summary>
    /// <param name="host">The rolled-back class host.</param>
    /// <returns>A non-degraded outcome.</returns>
    public static RollbackOutcome RolledBack(ServerHost host)
        => new(host, Degrade: null, TimeSpan.Zero);

    /// <summary>Creates the outcome of a degraded rollback.</summary>
    /// <param name="host">The freshly booted replacement host.</param>
    /// <param name="degrade">Why the rollback degraded.</param>
    /// <param name="recycleCost">Wall-clock cost of the fallback recycle.</param>
    /// <returns>A degraded outcome.</returns>
    public static RollbackOutcome DegradedToRecycle(
        ServerHost host, DegradeEvidence degrade, TimeSpan recycleCost)
        => new(host, degrade, recycleCost);
}
