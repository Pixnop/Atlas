using Atlas.Internal.Hosting;

namespace Atlas.XUnit.Internal;

/// <summary>The outcome of one <see cref="HostRegistry.RecycleAsync"/> call: the freshly booted
/// host plus the measured wall-clock cost of the recycle (dispose + boot), mirroring
/// <see cref="RestartOutcome"/>. A recycle is paid outside any scenario's timed body, so the
/// caller records the cost in the class's isolation tally (as a FreshWorld recycle, or as the
/// fallback cost of a degraded rollback) to keep it visible in the end-of-class summary
/// (issue #71: FreshWorld-only classes previously paid their recycles invisibly).</summary>
/// <param name="Host">The freshly booted host.</param>
/// <param name="Cost">Wall-clock cost of the recycle (dispose + boot).</param>
internal sealed record RecycleOutcome(ServerHost Host, TimeSpan Cost);
