using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

namespace Sample.Scenarios;

[Trait("Category", "E2E")]
public class MarkerScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task SampleModBlock_Should_BePlaceable_When_ModIsLoaded()
    {
        BlockPos pos = World.Spawn.Offset(1, 1, 0);
        World.SetBlock("samplemod:atlasmarker", pos);
        await World.Ticks(5);
        Assert.Equal("samplemod:atlasmarker", World.BlockAt(pos).Code.ToString());
    }

    [AtlasScenario]
    public async Task TimeCommand_Should_AdvanceCalendar_When_Executed()
    {
        double before = World.Calendar.TotalHours;
        World.ExecuteCommand("/time add 2");
        await World.Until(() => World.Calendar.TotalHours > before, timeoutTicks: 100);
    }
}
