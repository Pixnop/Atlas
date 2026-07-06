using Vintagestory.API.Config;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class DataSeedingTests : IDisposable
{
    private readonly DirectoryInfo _fixtureRoot = Directory.CreateTempSubdirectory("atlas-seed-");

    public void Dispose()
    {
        _fixtureRoot.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ServerHost_Should_ExposeSeededModConfig_When_DataFilesAreDeclared()
    {
        string fixture = _fixtureRoot.CreateSubdirectory("ModConfig").FullName;
        File.WriteAllText(
            Path.Combine(fixture, "atlas-seed-test.json"), """{ "greeting": "seeded-before-boot" }""");

        await using var host = new ServerHost(
            new WorldOptions(),
            Array.Empty<string>(),
            AppContext.BaseDirectory,
            new[] { new DataFileSeed(fixture, "ModConfig") });
        await host.StartAsync();

        await host.RunOnGameThreadAsync((api, _) =>
        {
            // The exact read path a mod's StartServerSide uses; also pin the physical location,
            // so a regression in where the seed lands cannot hide behind a lenient reader.
            SeedTestConfig? config = api.LoadModConfig<SeedTestConfig>("atlas-seed-test.json");
            Assert.NotNull(config);
            Assert.Equal("seeded-before-boot", config.Greeting);
            Assert.True(File.Exists(Path.Combine(GamePaths.ModConfig, "atlas-seed-test.json")));
            return Task.CompletedTask;
        });
    }

    private sealed class SeedTestConfig
    {
        public string Greeting { get; set; } = string.Empty;
    }
}
