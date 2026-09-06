using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class SmokeTests
{
    [Fact]
    public async Task ServerHost_Should_BootTickAssertAndStop_When_RunTwiceInProcess()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE any boot redirects it

        // What Atlas documents, and what every FreshWorld recycle relies on: "identical seeds
        // produce identical worlds" (WorldOptions.Seed), measured in docs/feasibility-spike.md as
        // bit-identical worldgen output across two independent creations at seed 424242. The three
        // values below are all read out of world generation. Two of them (terrain height, ground
        // block) are constant on this superflat world, so the one that actually discriminates the
        // seed is the climate map: it comes from seed-derived noise, and boots at a different seed
        // read a different temperature and fertility here. Deliberately NOT the calendar, which
        // advances on wall-clock time rather than on world generation.
        (int Height, string Ground, float Temperature, float Fertility)? firstRun = null;

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

                ClimateCondition climate = api.World.BlockAccessor.GetClimateAt(spawn);
                var worldgen = (y, block.Code.ToString(), climate.Temperature, climate.Fertility);
                if (firstRun is { } first)
                {
                    Assert.Equal(first, worldgen);
                }
                else
                {
                    firstRun = worldgen;
                }
            });
        }
    }

    [Fact]
    public async Task Boot_Should_SetCurrentDirectoryToInstall_When_HostStarts()
    {
        // Pins the issue #32 hardening: the engine's mod loader scans mod dlls with Mono.Cecil's
        // default resolver, which searches the process current directory. Runs where the test bin
        // held no VintagestoryAPI.dll copy used to load zero base mods and die in selectPlayStyle.
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();

        string install = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(install)),
            Path.TrimEndingDirectorySeparator(Directory.GetCurrentDirectory()));
    }
}
