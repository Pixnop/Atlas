namespace Atlas.Engine.Tests;

/// <summary>
/// Proves a prebuilt world save declared via <see cref="WorldOptions.SaveFile"/> is loaded
/// instead of a freshly generated world: a block placed in a first host's world survives into a
/// second host booted from that world's save. Also pins the fixture contract: any file name
/// works, and the fixture itself stays untouched.
/// </summary>
[Trait("Category", "E2E")]
public class WorldSaveTests : IDisposable
{
    private readonly DirectoryInfo _fixtureRoot = Directory.CreateTempSubdirectory("atlas-worldsave-");

    public void Dispose()
    {
        _fixtureRoot.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Host_Should_LoadPrebuiltWorldSave_When_SaveFileIsSet()
    {
        // The fixture name deliberately differs from the engine's pinned save name: the host
        // must rename the copy, not require pre-named fixtures.
        string fixture = Path.Combine(_fixtureRoot.FullName, "prebuilt-world.vcdbs");
        Vintagestory.API.MathTools.BlockPos? marker = null;

        // First host: place a marker block, then dispose gracefully; the engine's shutdown
        // persists the world into this host's scratch save.
        string builderDataPath;
        {
            await using var builder = new ServerHost(
                new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
            await builder.StartAsync();
            await builder.RunScenarioAsync(async world =>
            {
                marker = world.Spawn.Offset(2, 1, 0);
                world.SetBlock("game:soil-medium-normal", marker);
                await world.Ticks(5);
            });
            builderDataPath = builder.DataPath;
        }

        File.Copy(Path.Combine(builderDataPath, "Saves", "atlas.vcdbs"), fixture);
        DateTime fixtureStamp = File.GetLastWriteTimeUtc(fixture);

        // Second host: fresh scratch dir, booted from the fixture instead of world generation.
        await using var replayer = new ServerHost(
            new WorldOptions { SaveFile = fixture }, Array.Empty<string>(), AppContext.BaseDirectory);
        await replayer.StartAsync();
        await replayer.RunScenarioAsync(world =>
        {
            Assert.Equal("game:soil-medium-normal", world.BlockAt(marker!).Code.ToString());
            return Task.CompletedTask;
        });

        Assert.Equal(fixtureStamp, File.GetLastWriteTimeUtc(fixture));
    }

    [Fact]
    public async Task StartAsync_Should_FailWithSetupError_When_SaveFileDoesNotExist()
    {
        await using var host = new ServerHost(
            new WorldOptions { SaveFile = "no-such-world.vcdbs" },
            Array.Empty<string>(),
            AppContext.BaseDirectory);

        AtlasSetupException error = await Assert.ThrowsAsync<AtlasSetupException>(host.StartAsync);
        Assert.Contains("no-such-world.vcdbs", error.Message);
    }
}
