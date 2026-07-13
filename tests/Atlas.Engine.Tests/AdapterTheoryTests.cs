using Atlas.XUnit;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>The passing half of the <c>[AtlasTheory]</c> coverage, run directly under
/// <c>dotnet test</c> (the failing half lives in the guinea pig assembly and is asserted by
/// <c>TheoryNestedRunnerTests</c>): each <c>[InlineData]</c> row runs as its own scenario on the
/// game thread with the row's arguments bound, and per-row isolation settings really apply per
/// row (<c>RollbackWorld = true</c> rolls the world back before every row, not once per
/// theory).</summary>
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

    [AtlasTheory(RollbackWorld = true)]
    [InlineData("game:rock-granite")]
    [InlineData("game:soil-medium-normal")]
    public async Task Theory_Should_RollTheWorldBackPerRow_When_RollbackWorldIsRequested(string blockCode)
    {
        // Every row pollutes the SAME slot, so the air assertion below is the per-row rollback
        // proof: the first rollback-enabled row runs against the lazily captured snapshot (air),
        // and every later row only finds air again if the previous row's block was rolled back.
        BlockPos pos = World.Spawn.Offset(0, 4, 0);
        Assert.Equal(0, World.BlockAt(pos).BlockId);

        World.SetBlock(blockCode, pos);
        await World.Ticks(1);
        Assert.Equal(blockCode, World.BlockAt(pos).Code.ToString()); // the pollution really landed
    }
}
