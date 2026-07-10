using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class PlaceSchematicTests : IDisposable
{
    private readonly DirectoryInfo _fixtures = Directory.CreateTempSubdirectory("atlas-schematic-");

    public void Dispose()
    {
        _fixtures.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PlaceSchematic_Should_RestoreCapturedBlocks_When_AreaWasWiped()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), _fixtures.FullName);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Build a small asymmetric structure out of distinct block types.
            BlockPos corner = world.Spawn.Offset(2, 1, 2);
            var built = new Dictionary<(int X, int Y, int Z), string>
            {
                [(0, 0, 0)] = "game:soil-medium-normal",
                [(1, 0, 0)] = "game:rock-granite",
                [(0, 1, 1)] = "game:rock-andesite"
            };
            foreach (KeyValuePair<(int X, int Y, int Z), string> cell in built)
            {
                world.SetBlock(cell.Value, corner.Offset(cell.Key.X, cell.Key.Y, cell.Key.Z));
            }

            await world.Ticks(1);

            // Export it through the engine's own capture (end corner exclusive), the same
            // BlockSchematic pipeline worldedit's /we export uses.
            var captured = new BlockSchematic(world.Api.World, corner, corner.Offset(2, 2, 2), notLiquids: false);
            Assert.Null(captured.Save(Path.Combine(_fixtures.FullName, "structure.json")));

            WipeCube(world, corner, size: 2);
            Assert.Equal(0, world.BlockAt(corner).BlockId);

            // Relative path: resolves against the host's base directory (the fixtures dir here).
            int placed = world.PlaceSchematic("structure.json", corner);
            await world.Ticks(1);

            Assert.Equal(built.Count, placed);
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Block block = world.BlockAt(corner.Offset(x, y, z));
                        if (built.TryGetValue((x, y, z), out string? code))
                        {
                            Assert.Equal(code, block.Code.ToString());
                        }
                        else
                        {
                            Assert.Equal(0, block.BlockId);
                        }
                    }
                }
            }
        });
    }

    [Fact]
    public async Task PlaceSchematic_Should_HonorReplaceMode_When_ModeOverloadIsUsed()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), _fixtures.FullName);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            // Capture a 3x1x1 strip whose middle cell is air: two rocks with a gap.
            BlockPos corner = world.Spawn.Offset(0, 4, 0);
            world.SetBlock("game:rock-granite", corner);
            world.SetBlock("game:rock-granite", corner.Offset(2, 0, 0));
            await world.Ticks(1);
            var captured = new BlockSchematic(world.Api.World, corner, corner.Offset(3, 1, 1), notLiquids: false);
            Assert.Null(captured.Save(Path.Combine(_fixtures.FullName, "strip.json")));

            // Prefill two target strips completely, then place the schematic over both.
            BlockPos noAirTarget = world.Spawn.Offset(0, 6, 0);
            BlockPos replaceAllTarget = world.Spawn.Offset(0, 8, 0);
            foreach (BlockPos target in new[] { noAirTarget, replaceAllTarget })
            {
                for (int x = 0; x < 3; x++)
                {
                    world.SetBlock("game:soil-medium-normal", target.Offset(x, 0, 0));
                }
            }

            await world.Ticks(1);

            // The schematic's own default (ReplaceAllNoAir) skips its air cell...
            world.PlaceSchematic("strip.json", noAirTarget);
            Assert.Equal(
                "game:soil-medium-normal", world.BlockAt(noAirTarget.Offset(1, 0, 0)).Code.ToString());

            // ...while an explicit ReplaceAll stamps the full cuboid, air included.
            int placed = world.PlaceSchematic("strip.json", replaceAllTarget, EnumReplaceMode.ReplaceAll);
            await world.Ticks(1);

            Assert.Equal(2, placed);
            Assert.Equal("game:rock-granite", world.BlockAt(replaceAllTarget).Code.ToString());
            Assert.Equal(0, world.BlockAt(replaceAllTarget.Offset(1, 0, 0)).BlockId);
            Assert.Equal(
                "game:rock-granite", world.BlockAt(replaceAllTarget.Offset(2, 0, 0)).Code.ToString());
        });
    }

    [Fact]
    public async Task PlaceSchematic_Should_ThrowSetupException_When_FileIsMissing()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), _fixtures.FullName);
        await host.StartAsync();
        await host.RunScenarioAsync(world =>
        {
            AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
                () => world.PlaceSchematic("no-such-structure.json", world.Spawn));

            Assert.Contains("no-such-structure.json", ex.Message);
            Assert.Contains("does not exist", ex.Message);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PlaceSchematic_Should_ThrowSetupException_When_FileIsMalformed()
    {
        string garbage = Path.Combine(_fixtures.FullName, "garbage.json");
        await File.WriteAllTextAsync(garbage, "this is not a schematic {{{");

        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), _fixtures.FullName);
        await host.StartAsync();
        await host.RunScenarioAsync(world =>
        {
            // Absolute path: taken as-is, no base-directory resolution involved.
            AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
                () => world.PlaceSchematic(garbage, world.Spawn));

            Assert.Contains(garbage, ex.Message);
            Assert.Contains("Failed loading", ex.Message);
            return Task.CompletedTask;
        });
    }

    private static void WipeCube(IWorldSession world, BlockPos corner, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    world.Api.World.BlockAccessor.SetBlock(0, corner.Offset(x, y, z));
                }
            }
        }
    }
}
