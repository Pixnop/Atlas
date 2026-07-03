using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class SmokeTests
{
    [Fact]
    public async Task ServerHost_Should_BootTickAssertAndStop_When_RunTwiceInProcess()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE any boot redirects it
        for (int run = 1; run <= 2; run++)
        {
            await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
            await host.StartAsync();
            await host.RunOnGameThreadAsync(async (api, ticks) =>
            {
                await ticks.WaitTicksAsync(1);
                var spawn = api.World.DefaultSpawnPosition.AsBlockPos;
                int y = api.World.BlockAccessor.GetTerrainMapheightAt(spawn);
                var block = api.World.BlockAccessor.GetBlock(new BlockPos(spawn.X, y, spawn.Z, spawn.dimension));
                Assert.NotNull(block.Code);
                Assert.Equal(424242, api.World.Seed);
            });
        }
    }
}
