using Atlas.Api;

namespace Atlas.Engine.Tests;

/// <summary>Covers <see cref="IWorldSession.MeasureTicks"/> end to end, against a REAL
/// server: a deliberately expensive mod tick listener (TickTimingFixtureMod, staged like the
/// other single-purpose fixture mods in this folder), and the vanilla case it is compared
/// against. See docs/specs/2026-09-23-tick-timing.md for the methodology and the noise this
/// suite's own repeated runs measured on the CI machine.</summary>
[Trait("Category", "E2E")]
public class TickTimingTests
{
    private const string FixtureModDll = "TickTimingFixtureMod.dll";

    [Fact]
    public async Task MeasureTicks_Should_SeeTheFixtureModsSpin_When_ItIsStaged()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Settle: the mod's own listener registration and the first few passes after boot
            // can be pulled off-cadence by the boot's tail end.
            await world.Ticks(20);

            TickMeasurement measured = await world.MeasureTicks(30);

            // The window's pass count tracks the requested tick count under the engine's
            // default pacing (docs/specs/2026-07-14-tick-contract.md), but this asserts the
            // measured relationship, not an assumed exact equality - a wide band absorbs CI
            // jitter without hiding a real regression (an order of magnitude off).
            Assert.InRange(measured.Passes, 30 / 2, 30 * 2);

            // The fixture spins for TickTimingFixtureMod.SpinMilliseconds (20ms) every tick;
            // a wide margin below that (not the exact figure) keeps this from flaking on a
            // loaded CI runner while still failing hard if MeasureTicks stopped seeing the
            // engine's own busy-time record at all (which would read 0, not "close to 20").
            string busyDetail = $"min={measured.BusyTime.MinMs} median={measured.BusyTime.MedianMs} " +
                $"p95={measured.BusyTime.P95Ms} max={measured.BusyTime.MaxMs}";
            Assert.True(measured.BusyTime.MedianMs >= 10, $"expected the fixture's ~20ms spin to dominate the median pass ({busyDetail})");
            Assert.True(
                measured.BusyTime.MaxMs >= measured.BusyTime.MedianMs,
                "max must be at least the median in any non-empty window");

            // The spin's own Stopwatch.StartNew() allocates every tick; a non-zero delta proves
            // the game-thread allocation reading is wired up, not just always zero.
            Assert.True(measured.AllocatedBytes > 0, $"expected game-thread allocations, got {measured.AllocatedBytes}");

            // Wall time includes the engine's pacing sleep, so it is at least the busy time
            // summed, and (loosely) on the order of one pacing interval per pass.
            Assert.True(measured.WallTime.TotalMilliseconds >= measured.BusyTime.MedianMs);
        });
    }

    [Fact]
    public async Task MeasureTicks_Should_ReportALowMedian_When_NoModIsStaged()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.Ticks(20);

            TickMeasurement measured = await world.MeasureTicks(30);

            // Not asserted at 0: engine bookkeeping and Atlas's own instrumentation still cost
            // something, and CI runners vary. The point of this test is the CONTRAST with the
            // fixture-mod case above, measured together in docs/specs/2026-09-23-tick-timing.md;
            // here it only pins that an idle world stays far under the fixture's 20ms spin.
            string tooSlow = $"expected an idle world's median pass to stay well under the fixture's 20ms " +
                $"spin, got {measured.BusyTime.MedianMs}ms";
            Assert.True(measured.BusyTime.MedianMs < 10, tooSlow);
        });
    }
}
