using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins the issue #79 contract: <c>EntitySimulationTicks</c> counts the engine's real
/// entity-simulation ticks, so entity-tick-frequency probes (the StratumParity pattern) can
/// assert exact counts instead of the ratio workarounds the varying
/// <c>Ticks(n)</c>-to-entity-ticks mapping forced. See docs/specs/2026-07-14-tick-contract.md.</summary>
[Trait("Category", "E2E")]
public class EntitySimulationTickTests
{
    [Fact]
    public async Task EntitySimulationTicks_Should_AdvanceMonotonically_When_AwaitingTicks()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            long atBoot = world.EntitySimulationTicks;
            await world.Ticks(30);
            long afterWarmup = world.EntitySimulationTicks;
            await world.Ticks(60);
            long afterWindow = world.EntitySimulationTicks;

            // The counter starts at boot (the pump samples from the first Process pass, and
            // the entity simulation fires on any pass 20ms of unpaused clock after the
            // previous fire, so a multi-second boot has simulated long before a scenario runs).
            Assert.True(atBoot > 0, $"counter did not advance during boot (atBoot={atBoot})");
            Assert.True(afterWarmup > atBoot, $"not monotonic: {afterWarmup} after {atBoot}");
            Assert.True(afterWindow > afterWarmup, $"not monotonic: {afterWindow} after {afterWarmup}");

            // Loosely bounded on purpose: the point is that the counter tracks real simulation
            // progress across the awaited window, not that the engine keeps any exact ratio
            // between harness ticks and simulation ticks (it does not promise one; that ratio
            // varying is the issue #79 field report). Under the engine's default 33.33ms pacing
            // the two run 1:1, so [N/4, 4N] absorbs pathological CI jitter in both directions.
            long delta = afterWindow - afterWarmup;
            Assert.InRange(delta, 60 / 4, 60 * 4);
        });
    }

    [Fact]
    public async Task EntitySimulationTicks_Should_MatchAnEntityTickProbeExactly_When_MeasuredOverAWindow()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Mirrors the StratumParity entity-tick-frequency probe: a player as presence
            // anchor and a counting behavior on a spawned straw dummy (stationary by design).
            // The dummy is placed next to the player's ACTUAL entity, not next to world spawn:
            // new players spawn randomized within the world's spawnRadius (50 blocks on the
            // default playstyle), which is exactly the run-to-run variance that made the field
            // suite's spawn-anchored probes land in different distance bands per run.
            ITestPlayer player = await world.JoinPlayer("tick-anchor");
            BlockPos probePos = player.Entity.Pos.AsBlockPos.Add(2, 1, 0);
            Entity dummy = world.SpawnEntity("game:strawdummy", probePos);
            var probe = new TickCountingBehavior(dummy);
            dummy.SidedProperties.Behaviors.Add(probe);

            // Settle: the engine refreshes its cached per-entity behavior list on the next
            // entity tick after the attach, and the dummy drops onto the ground block.
            await world.Ticks(30);

            long simBefore = world.EntitySimulationTicks;
            int probeBefore = probe.Ticks;
            await world.Ticks(90);
            long simDelta = world.EntitySimulationTicks - simBefore;
            int probeDelta = probe.Ticks - probeBefore;

            // Exact, not a ratio: on every supported vanilla engine, one entity-simulation
            // tick calls Entity.OnGameTick exactly once per loaded dimension-0 entity, and the
            // counter and the probe are read in the same game-thread turn, so their windows
            // align tick-for-tick. This is the assertion the issue #79 field report could not
            // write against Ticks(n).
            Assert.True(simDelta > 0, $"the simulation never ticked across the window (simDelta={simDelta})");
            Assert.Equal(simDelta, probeDelta);
        });
    }

    /// <summary>Counts how often the server really ticks an entity: <c>Entity.OnGameTick</c>
    /// drives behavior ticks, so this counts entity-simulation ticks as observed by one
    /// entity (the StratumParity <c>TickCounterBehavior</c> pattern).</summary>
    private sealed class TickCountingBehavior : EntityBehavior
    {
        private int ticks;

        public TickCountingBehavior(Entity entity)
            : base(entity)
        {
        }

        public int Ticks => ticks;

        public override void OnGameTick(float deltaTime) => ticks++;

        public override string PropertyName() => "atlas:tickprobe";
    }
}
