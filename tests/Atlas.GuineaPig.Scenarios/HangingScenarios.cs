using Atlas.XUnit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Exercises the full wall-clock watchdog path end-to-end: a scenario that genuinely
/// wedges the game thread past its <c>TimeoutMs</c> must FAIL with
/// <c>ScenarioTimeoutException</c>, raised by the invoker's watchdog - not hang the suite.
/// The direct-engine variant (<c>WatchdogTimeoutTests</c> in Atlas.Engine.Tests) drives
/// <c>Watchdog.RunAsync</c> by hand; this one goes through <c>[AtlasScenario]</c> itself.</summary>
[AtlasWorld(Seed = 920)]
public class HangingScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 2000)]
    public async Task Scenario_Should_TimeOut_When_GameThreadWedges()
    {
        Thread.Sleep(8000); // wedges the pump: the exact hang the watchdog exists to catch
        await World.Ticks(1);
    }
}
