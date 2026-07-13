using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

namespace Sample.Scenarios;

/// <summary>Shows <c>[AtlasTheory]</c>: the same scenario body run once per data row, each row
/// its own scenario on the game thread with the row's values in its display name. Uses only
/// vanilla blocks, so no mod is required.</summary>
[Trait("Category", "E2E")]
public class ParameterizedScenarios : AtlasScenarioBase
{
    [AtlasTheory]
    [InlineData("game:chest-east")]
    [InlineData("game:soil-medium-normal")]
    [InlineData("game:rock-granite")]
    public async Task VanillaBlock_Should_BePlaceable_When_CodeComesFromDataRow(string blockCode)
    {
        BlockPos pos = World.Spawn.Offset(1, 1, 0);
        World.SetBlock(blockCode, pos);
        await World.Ticks(5);
        Assert.Equal(blockCode, World.BlockAt(pos).Code.ToString());
    }
}
