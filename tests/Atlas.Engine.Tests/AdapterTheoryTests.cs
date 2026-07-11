using Atlas.XUnit;

namespace Atlas.Engine.Tests;

/// <summary>The passing half of the <c>[AtlasTheory]</c> coverage, run directly under
/// <c>dotnet test</c> (the failing half lives in the guinea pig assembly and is asserted by
/// <c>TheoryNestedRunnerTests</c>): each <c>[InlineData]</c> row runs as its own scenario on the
/// game thread with the row's arguments bound.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 778)]
public class AdapterTheoryTests : AtlasScenarioBase
{
    [AtlasTheory]
    [InlineData("game:soil-medium-normal")]
    [InlineData("game:rock-granite")]
    public async Task Theory_Should_ReceiveRowArguments_When_EachRowRunsAsScenario(string blockCode)
    {
        var pos = World.Spawn.Offset(0, 2, 0);
        World.SetBlock(blockCode, pos);
        await World.Ticks(1);
        Assert.Equal(blockCode, World.BlockAt(pos).Code.ToString());
    }
}
