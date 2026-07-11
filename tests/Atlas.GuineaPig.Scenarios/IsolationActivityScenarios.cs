using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Isolation-activity guinea pig for the summary observability paths (issue #66):
/// one passing RollbackWorld scenario and one passing RestartWorld scenario, so the class's
/// isolation summary reports a rollback AND a restart with its measured cost. Unlike the other
/// guinea pigs these scenarios PASS: WorkerModeTests asserts the class-summary protocol event
/// they produce, ParallelModeTests asserts the orchestrator's aggregated summary, and
/// NestedRunnerTests counts them as the suite's only passing scenarios. The orderer makes the
/// rollback-then-restart sequence deterministic (the restart must not shut the host down
/// before the rollback scenario used it).</summary>
[TestCaseOrderer("Atlas.GuineaPig.Scenarios.AlphabeticalOrderer", "Atlas.GuineaPig.Scenarios")]
[AtlasWorld(Seed = 924)]
public class IsolationActivityScenarios : AtlasScenarioBase
{
    [AtlasScenario(RollbackWorld = true)]
    public async Task A_Scenario_Should_Pass_When_RollbackWorldIsRequested()
    {
        await World.Ticks(1);
        Assert.NotNull(World.Spawn);
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task B_Scenario_Should_Pass_When_RestartWorldIsRequested()
    {
        await World.Ticks(1);
        Assert.NotNull(World.Spawn);
    }
}
