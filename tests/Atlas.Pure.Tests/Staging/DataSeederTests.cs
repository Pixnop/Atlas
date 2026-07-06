using Atlas.Api;
using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class DataSeederTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-seeder-");
    private readonly string _baseDir;
    private readonly string _dataPath;

    public DataSeederTests()
    {
        _baseDir = _root.CreateSubdirectory("base").FullName;
        _dataPath = Path.Combine(_root.FullName, "data");
    }

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Seed_Should_CopyDirectoryContentsIntoTargetPath_When_SourceIsDirectory()
    {
        string fixture = Path.Combine(_baseDir, "fixtures", "ModConfig");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "mymod.json"), "{}");

        DataSeeder.Seed(
            [new DataFileSeed(Path.Combine("fixtures", "ModConfig"), "ModConfig")], _baseDir, _dataPath);

        Assert.True(File.Exists(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
    }

    [Fact]
    public void Seed_Should_OverlayTreeOntoDataPathRoot_When_TargetPathIsEmpty()
    {
        string overlay = Path.Combine(_baseDir, "serverdata");
        Directory.CreateDirectory(Path.Combine(overlay, "ModConfig"));
        Directory.CreateDirectory(Path.Combine(overlay, "Macros"));
        File.WriteAllText(Path.Combine(overlay, "ModConfig", "mymod.json"), "{}");
        File.WriteAllText(Path.Combine(overlay, "Macros", "macro.json"), "{}");

        DataSeeder.Seed([new DataFileSeed("serverdata")], _baseDir, _dataPath);

        Assert.True(File.Exists(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
        Assert.True(File.Exists(Path.Combine(_dataPath, "Macros", "macro.json")));
    }

    [Fact]
    public void Seed_Should_CopyFileUnderItsOwnName_When_SourceIsFile()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");

        DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath);

        Assert.True(File.Exists(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
    }

    [Fact]
    public void Seed_Should_LetLaterSeedWin_When_TwoSeedsCollideOnTargetFile()
    {
        string first = _root.CreateSubdirectory("first").FullName;
        string second = _root.CreateSubdirectory("second").FullName;
        File.WriteAllText(Path.Combine(first, "mymod.json"), "first");
        File.WriteAllText(Path.Combine(second, "mymod.json"), "second");

        DataSeeder.Seed(
            [new DataFileSeed(first, "ModConfig"), new DataFileSeed(second, "ModConfig")],
            _baseDir,
            _dataPath);

        Assert.Equal("second", File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
    }

    [Fact]
    public void Seed_Should_ThrowListingAllMissingPaths_When_SourcesDoNotExist()
    {
        File.WriteAllText(Path.Combine(_baseDir, "present.json"), "{}");

        var ex = Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [
                new DataFileSeed("ghost.json", "ModConfig"),
                new DataFileSeed("present.json", "ModConfig"),
                new DataFileSeed("phantom", "ModConfig"),
            ],
            _baseDir,
            _dataPath));

        Assert.Contains("ghost.json", ex.Message);
        Assert.Contains("phantom", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dataPath, "ModConfig", "present.json")));
    }

    [Fact]
    public void SeedWorldSave_Should_CopyUnderPinnedSaveName_When_FixtureHasAnotherName()
    {
        File.WriteAllText(Path.Combine(_baseDir, "prebuilt-world.vcdbs"), "save-bytes");

        DataSeeder.SeedWorldSave("prebuilt-world.vcdbs", _baseDir, _dataPath);

        Assert.Equal(
            "save-bytes",
            File.ReadAllText(Path.Combine(_dataPath, "Saves", DataSeeder.WorldSaveFileName)));
    }

    [Fact]
    public void SeedWorldSave_Should_OverwriteExistingSave_When_ADataFileSeedPlacedOneBefore()
    {
        File.WriteAllText(Path.Combine(_baseDir, DataSeeder.WorldSaveFileName), "raw-seeded");
        File.WriteAllText(Path.Combine(_baseDir, "explicit.vcdbs"), "explicit-save");
        DataSeeder.Seed([new DataFileSeed(DataSeeder.WorldSaveFileName, "Saves")], _baseDir, _dataPath);

        DataSeeder.SeedWorldSave("explicit.vcdbs", _baseDir, _dataPath);

        Assert.Equal(
            "explicit-save",
            File.ReadAllText(Path.Combine(_dataPath, "Saves", DataSeeder.WorldSaveFileName)));
    }

    [Fact]
    public void SeedWorldSave_Should_ThrowNamingThePath_When_FixtureDoesNotExist()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => DataSeeder.SeedWorldSave("no-such-world.vcdbs", _baseDir, _dataPath));

        Assert.Contains("no-such-world.vcdbs", ex.Message);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escaped")]
    public void Seed_Should_ThrowSetupException_When_TargetPathEscapesDataPath(string targetPath)
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");

        Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [new DataFileSeed("mymod.json", targetPath)], _baseDir, _dataPath));
    }

    [Fact]
    public void Seed_Should_ThrowSetupException_When_TargetPathIsRooted()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");
        string rooted = _root.CreateSubdirectory("elsewhere").FullName;

        Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [new DataFileSeed("mymod.json", rooted)], _baseDir, _dataPath));
    }

    [Fact]
    public void Seed_Should_DoNothing_When_NoSeedsAreDeclared()
    {
        DataSeeder.Seed([], _baseDir, _dataPath);

        Assert.False(Directory.Exists(_dataPath));
    }
}
