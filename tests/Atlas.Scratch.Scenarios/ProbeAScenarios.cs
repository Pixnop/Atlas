using Atlas.XUnit;
using Xunit;

namespace Atlas.Scratch.Scenarios;

/// <summary>First probe class.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 711)]
public class ProbeAScenarios : ProbeScenariosBase
{
    [AtlasScenario]
    public Task Probe_Should_Tick_When_TheHostIsUp() => TickAndMaybeFailAsync(nameof(ProbeAScenarios));
}
