using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class FixtureScenarioSelectionTests
{
    [Fact]
    public void Validate_Should_ReturnNull_When_ExactlyOneScenarioMatches()
    {
        var matches = new[] { new DiscoveredScenario("Ns.Builders", "Ns.Builders.BuildsTheWorld") };

        Assert.Null(FixtureScenarioSelection.Validate(matches, "BuildsTheWorld"));
    }

    [Fact]
    public void Validate_Should_NameTheSubstring_When_NothingMatches()
    {
        string? error = FixtureScenarioSelection.Validate([], "Chest");

        Assert.NotNull(error);
        Assert.Contains("no scenario matches", error);
        Assert.Contains("'Chest'", error);
        Assert.Contains("--list", error);
    }

    [Fact]
    public void Validate_Should_ListEveryCandidate_When_SeveralScenariosMatch()
    {
        var matches = new[]
        {
            new DiscoveredScenario("Ns.A", "Ns.A.Builds_Small_World"),
            new DiscoveredScenario("Ns.A", "Ns.A.Builds_Large_World"),
            new DiscoveredScenario("Ns.B", "Ns.B.Builds_Ocean_World"),
        };

        string? error = FixtureScenarioSelection.Validate(matches, "Builds");

        Assert.NotNull(error);
        Assert.Contains("matches 3 scenarios", error);
        Assert.Contains("exactly one", error);
        Assert.Contains("Ns.A.Builds_Small_World", error);
        Assert.Contains("Ns.A.Builds_Large_World", error);
        Assert.Contains("Ns.B.Builds_Ocean_World", error);
    }
}
