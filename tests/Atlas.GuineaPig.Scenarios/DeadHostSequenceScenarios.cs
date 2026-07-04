using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Exercises the invoker-driven dead-host fail-fast path end-to-end: scenario A
/// crashes the class host's game thread; scenario B, running later on the same (now dead)
/// class host, must FAIL FAST with <c>ServerCrashedException</c> naming the dead host,
/// instead of trying to boot or reuse it. The direct-registry variant
/// (<c>DeadHostFailFastTests</c> in Atlas.Engine.Tests) drives <c>HostRegistry</c> by hand;
/// this one goes through the invoker. The orderer makes the A-then-B sequence deterministic.
/// Note the exact recovery route the nested run pins down: the crashing scenario's own await
/// continuation dies with the game thread, so it is the WATCHDOG that recovers scenario A
/// (marking the host abandoned) and <c>WrapCrashIfAny</c> that surfaces the true crash - which
/// is why A carries a short <c>TimeoutMs</c> and B's fail-fast message names the abandonment.</summary>
[TestCaseOrderer("Atlas.GuineaPig.Scenarios.AlphabeticalOrderer", "Atlas.GuineaPig.Scenarios")]
[AtlasWorld(Seed = 921)]
public class DeadHostSequenceScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 5000)]
    public async Task A_Scenario_Should_Crash_When_PoisonCallbackKillsThePump()
    {
        // The poison must reach the RAW GameThreadScheduler queue, whose drain runs outside any
        // catch (CrashSurfacingTests' technique). Two tempting shortcuts do not work here:
        // SynchronizationContext.Current inside an [AtlasScenario] body is xUnit's
        // AsyncTestSyncContext (installed by the base invoker around the reflected call), which
        // swallows posted exceptions; and anything thrown inside the engine (tick listeners,
        // EnqueueMainThreadTask) is eaten by ServerMain.Process()'s own catch-and-log-fatal.
        // RunOnGameThreadAsync's work delegate, by contrast, runs with the raw scheduler as
        // Current, so a poison posted from there lands on the unprotected drain.
        var host = await HostRegistry.GetOrCreateAsync(typeof(DeadHostSequenceScenarios));
        _ = host.RunOnGameThreadAsync((api, ticks) =>
        {
            SynchronizationContext.Current!.Post(
                _ => throw new InvalidOperationException("guinea-pig induced crash"),
                null);
            return Task.CompletedTask;
        });

        await World.Ticks(600);
    }

    [AtlasScenario]
    public async Task B_Scenario_Should_FailFast_When_ClassHostAlreadyCrashed()
    {
        await World.Ticks(1);
    }
}
