using System.Diagnostics.CodeAnalysis;
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
    [SuppressMessage(
        "Blocker Code Smell",
        "S2699:Tests should include assertions",
        Justification = "Deliberately failing guinea pig fixture: the body must run to wedge the game thread past the watchdog, so control never returns to an assertion. NestedRunnerTests runs this assembly nested and asserts the exact ScenarioTimeoutException shape.")]
    [SuppressMessage(
        "Major Code Smell",
        "S2925:\"Thread.Sleep\" should not be used in tests",
        Justification = "The sleep IS the tested behavior: it wedges the game thread inside a single pump iteration, the exact hang the wall-clock watchdog exists to catch. Any awaitable wait would yield the pump and defeat the fixture.")]
    public async Task Scenario_Should_TimeOut_When_GameThreadWedges()
    {
        Thread.Sleep(8000); // wedges the pump: the exact hang the watchdog exists to catch
        await World.Ticks(1);
    }
}
