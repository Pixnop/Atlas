using System.Reflection;
using Atlas.Api;

namespace Atlas.Engine.Tests;

/// <summary>Covers <see cref="IWorldSession.MeasureTicks"/> end to end, against a REAL
/// server: a deliberately expensive mod tick listener (TickTimingFixtureMod, staged like the
/// other single-purpose fixture mods in this folder), and the vanilla case it is compared
/// against. See docs/specs/2026-09-23-tick-timing.md for the methodology and the noise this
/// suite's own repeated runs measured locally (see that spec's "Measured: noise on this
/// machine").</summary>
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

            // The mean and the total see the same spin: a wide margin below 20ms again (the
            // mean is of whole-millisecond samples, so it is a floor of the true mean), and the
            // total is the mean over the same passes, so one is exactly the other.
            Assert.True(measured.BusyTime.MeanMs >= 10, $"expected the fixture's ~20ms spin to dominate the mean pass ({busyDetail})");
            Assert.InRange(measured.BusyTime.MeanMs, measured.BusyTime.MinMs, measured.BusyTime.MaxMs);
            Assert.Equal(measured.BusyTime.TotalMs / (double)measured.Passes, measured.BusyTime.MeanMs);
            Assert.True(measured.BusyTime.TotalMs >= measured.BusyTime.MaxMs, "the total includes the slowest pass");

            // The engine's own per-pass work, plus the spin's own Stopwatch.StartNew(), both
            // allocate on the game thread every tick; a non-zero delta just proves the
            // allocation reading is wired up, not that it is all attributable to the spin.
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
            Assert.True(
                measured.BusyTime.MeanMs < 10,
                $"expected an idle world's mean pass to stay well under the fixture's 20ms spin, got {measured.BusyTime.MeanMs}ms");
        });
    }

    [Fact]
    public async Task MeasureTicks_Should_MatchTheEnginesOwnTickTimeTotalAndTicksTotal_When_NoTwoSecondBucketRolledOver()
    {
        await using ServerHost host = TestHosts.New(FixtureModDll);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.Ticks(20);

            // The engine adds each pass's busy time to tickTimeTotal and counts it in ticksTotal,
            // the same value MeasureTicks samples, so over a window inside one stats bucket the
            // growth of the two counters is exactly TotalMs and Passes. The engine moves to a
            // fresh, zeroed bucket every two seconds, and a window that spans the move has
            // nothing to compare against: such a window is retried (a 10-pass window is about a
            // third of a second, so most are clean).
            for (int attempt = 0; attempt < 20; attempt++)
            {
                (int bucketBefore, long timeBefore, long ticksBefore) = ReadEngineTickCounters(world);
                TickMeasurement measured = await world.MeasureTicks(10);
                (int bucketAfter, long timeAfter, long ticksAfter) = ReadEngineTickCounters(world);

                if (bucketBefore != bucketAfter)
                {
                    continue;
                }

                Assert.Equal(ticksAfter - ticksBefore, measured.Passes);
                Assert.Equal(timeAfter - timeBefore, measured.BusyTime.TotalMs);
                Assert.Equal((double)(timeAfter - timeBefore) / (ticksAfter - ticksBefore), measured.BusyTime.MeanMs);

                // Not a vacuous match of two zeros: the fixture's spin put real time in both.
                Assert.True(measured.BusyTime.TotalMs > 0, "expected the fixture's spin in the total");
                return;
            }

            Assert.Fail("every one of 20 windows spanned the engine's two-second stats rollover");
        });
    }

    /// <summary>Reads the engine's own running pair for the current stats bucket:
    /// <c>ServerMain.StatsCollector[StatsCollectorIndex]</c>'s <c>tickTimeTotal</c> and
    /// <c>ticksTotal</c>, plus which bucket that is. Through reflection on purpose, like the
    /// engine reads in <c>PlayingStateTests</c>: a VintagestoryLib type in a test body would
    /// resolve at JIT time, before a host has installed Atlas's AssemblyResolve hook.</summary>
    private static (int Bucket, long TickTimeTotal, long TicksTotal) ReadEngineTickCounters(IWorldSession world)
    {
        object server = world.Api.World;
        int bucket = (int)EngineField(server, "StatsCollectorIndex");
        object collection = ((Array)EngineField(server, "StatsCollector")).GetValue(bucket)!;
        return (bucket, (long)EngineField(collection, "tickTimeTotal"), (long)EngineField(collection, "ticksTotal"));
    }

    private static object EngineField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{name} not found; the engine shape drifted.");
        return field.GetValue(target)!;
    }
}
