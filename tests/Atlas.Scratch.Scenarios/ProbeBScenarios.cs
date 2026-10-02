using Atlas.XUnit;
using Xunit;

namespace Atlas.Scratch.Scenarios;

/// <summary>Second probe class.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 722)]
public class ProbeBScenarios : ProbeScenariosBase
{
    [AtlasScenario]
    public Task Probe_Should_Tick_When_TheHostIsUp() => TickAndMaybeFailAsync(nameof(ProbeBScenarios));
}
