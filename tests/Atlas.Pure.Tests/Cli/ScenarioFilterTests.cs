using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class ScenarioFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_Should_AcceptEverything_When_PatternIsNullOrEmpty(string? pattern)
    {
        var filter = new ScenarioFilter(pattern);

        Assert.False(filter.IsSelective);
        Assert.True(filter.Matches("Any.Class.Method"));
    }

    [Fact]
    public void Matches_Should_AcceptName_When_SubstringOccursAnywhere()
    {
        var filter = new ScenarioFilter("Transit");

        Assert.True(filter.Matches("Mod.TransitScenarios.Item_survives_dimension_transit"));
    }

    [Fact]
    public void Matches_Should_IgnoreCase_When_Comparing()
    {
        var filter = new ScenarioFilter("chest");

        Assert.True(filter.Matches("MarkerScenarios.Chest_Should_BePlaceable_When_WorldIsReady"));
    }

    [Fact]
    public void Matches_Should_RejectName_When_SubstringIsAbsent()
    {
        var filter = new ScenarioFilter("Transit");

        Assert.True(filter.IsSelective);
        Assert.False(filter.Matches("MarkerScenarios.Chest_Should_BePlaceable_When_WorldIsReady"));
    }
}
