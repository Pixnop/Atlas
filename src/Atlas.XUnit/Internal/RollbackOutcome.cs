using Atlas.Internal.Hosting;
using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>The outcome of one <see cref="HostRegistry.RollbackOrRecycleAsync"/> call: the host
/// the scenario must run on, plus whether the rollback request had to degrade to a full host
/// recycle, with the structured reason, the one-line detail and the measured cost of the
/// fallback recycle. The invoker turns a degraded outcome into the scenario's isolation report
/// (and, under strict isolation, into a failure).</summary>
/// <param name="Host">The host the scenario runs on: the rolled-back class host, or the freshly
/// booted replacement when the request degraded.</param>
/// <param name="Degraded">Whether the rollback request fell back to a full host recycle.</param>
/// <param name="DegradeReason">The structured degrade reason; only meaningful when
/// <paramref name="Degraded"/> is <see langword="true"/>.</param>
/// <param name="DegradeDetail">The one-line failure detail ("ExceptionType: message"), or
/// <see langword="null"/> when the rollback succeeded.</param>
/// <param name="RecycleCost">Wall-clock cost of the fallback recycle (dispose + boot), or
/// <see cref="TimeSpan.Zero"/> when the rollback succeeded.</param>
internal sealed record RollbackOutcome(
    ServerHost Host,
    bool Degraded,
    RollbackDegradeReason DegradeReason,
    string? DegradeDetail,
    TimeSpan RecycleCost)
{
    /// <summary>Creates the outcome of a successful rollback.</summary>
    /// <param name="host">The rolled-back class host.</param>
    /// <returns>A non-degraded outcome.</returns>
    public static RollbackOutcome RolledBack(ServerHost host)
        => new(host, Degraded: false, default, DegradeDetail: null, TimeSpan.Zero);

    /// <summary>Creates the outcome of a degraded rollback.</summary>
    /// <param name="host">The freshly booted replacement host.</param>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="detail">The one-line failure detail.</param>
    /// <param name="recycleCost">Wall-clock cost of the fallback recycle.</param>
    /// <returns>A degraded outcome.</returns>
    public static RollbackOutcome DegradedToRecycle(
        ServerHost host, RollbackDegradeReason reason, string detail, TimeSpan recycleCost)
        => new(host, Degraded: true, reason, detail, recycleCost);
}
