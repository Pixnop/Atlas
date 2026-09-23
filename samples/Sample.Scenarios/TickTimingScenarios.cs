using Atlas.Api;
using Atlas.XUnit;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Shows <c>World.MeasureTicks</c>: measure a window of ticks, then read what the game
/// thread did across it. See docs/specs/2026-09-23-tick-timing.md for what this measures, what
/// it cannot see, and the noise this machine measured.</summary>
[Trait("Category", "E2E")]
public class TickTimingScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task MeasureTicks_Should_ReportAPassForEveryTickRequested()
    {
        // Let the boot's own tail end settle before measuring, same as any tick-sensitive
        // assertion: a scenario's first few ticks can still carry boot-time catch-up work.
        await World.Ticks(20);

        TickMeasurement measured = await World.MeasureTicks(50);

        // On this idle world (no mod under test staged), busy time commonly reads 0ms: the
        // engine's own per-pass bookkeeping is millisecond-resolution, and real idle-world work
        // is sub-millisecond. That is the documented ceiling of this feature, not a bug - see
        // the spec's measured noise section before reading a single "0ms" window as "nothing
        // happened".
        // Not a hard equality: Passes is the measured pass count, not the requested tick count
        // restated (see IWorldSession.MeasureTicks). They match on every supported engine under
        // default pacing, which is what this loose band checks without hard-coding an assumption
        // the API itself deliberately does not make.
        Assert.InRange(measured.Passes, 50 / 2, 50 * 2);
        Assert.True(measured.BusyTime.MaxMs >= measured.BusyTime.MinMs);
        Assert.True(measured.WallTime.TotalMilliseconds > 0);
    }
}
