using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Isolation-activity guinea pig for the summary observability paths (issues #66 and
/// #71): two passing RollbackWorld scenarios (the first request captures the snapshot, the
/// second restores it, so the summary shows the capture as its own line item AND a genuine
/// rollback) plus one passing RestartWorld scenario with its measured cost. Unlike the other
/// guinea pigs these scenarios PASS: WorkerModeTests asserts the class-summary protocol event
/// they produce, ParallelModeTests asserts the orchestrator's aggregated summary, and
/// NestedRunnerTests counts them as the suite's only passing scenarios. The orderer makes the
/// capture-restore-restart sequence deterministic (the restart must not shut the host down
/// before the rollback scenarios used it).</summary>
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

    [AtlasScenario(RollbackWorld = true)]
    public async Task A_Scenario_Should_Pass_When_RollbackWorldRestores()
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
