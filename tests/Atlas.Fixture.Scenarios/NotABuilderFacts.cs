using Xunit;

namespace Atlas.Fixture.Scenarios;

/// <summary>Not a builder: a plain xunit fact passes without ever booting an Atlas host, so
/// `atlas fixture` has no world save to harvest from it and must exit 1 without writing
/// anything (FixtureCommandTests pins that path). It documents the authoring trap the error
/// message points at: a builder must be an [AtlasScenario] on an AtlasScenarioBase class.</summary>
public class NotABuilderFacts
{
    [Fact]
    public void PlainFact_Should_PassWithoutBootingAnyHost()
    {
        Assert.NotNull(typeof(NotABuilderFacts).Assembly);
    }
}
