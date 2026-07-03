using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class WorldSessionTests
{
    [Fact]
    public async Task WorldSession_Should_PlaceTickAndQueryBlock_When_ScenarioRuns()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            BlockPos pos = world.Spawn.Offset(2, 1, 0);
            world.SetBlock("game:soil-medium-normal", pos);
            await world.Ticks(5);
            Assert.Equal("game:soil-medium-normal", world.BlockAt(pos).Code.ToString());
            await world.Until(() => world.Calendar.TotalHours > 0, timeoutTicks: 100);
        });
    }

    [Fact]
    public async Task EntitiesIn_Should_MatchCuboidiOverload_When_QueriedWithDimensionZeroWorldArea()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            BlockPos pos = world.Spawn.Offset(3, 1, 0);
            Entity spawned = world.SpawnEntity("game:chicken-rooster", pos);
            await world.Ticks(2);

            WorldArea area = pos.Area(2);
            Assert.Equal(0, area.Dimension);

            IReadOnlyList<Entity> viaWorldArea = world.EntitiesIn(area);
            IReadOnlyList<Entity> viaCuboidi = world.EntitiesIn(area.Bounds);

            Assert.Contains(spawned, viaWorldArea);
            Assert.Equal(viaCuboidi.Select(e => e.EntityId), viaWorldArea.Select(e => e.EntityId));
        });
    }
}
