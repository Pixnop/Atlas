using Atlas.Api;
using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class DataSeederTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-seeder-");
    private readonly string _baseDir;
    private readonly string _dataPath;
    private readonly DataFilePorts _ports = new();

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
            [new DataFileSeed(Path.Combine("fixtures", "ModConfig"), "ModConfig")], _baseDir, _dataPath, _ports);

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

        DataSeeder.Seed([new DataFileSeed("serverdata")], _baseDir, _dataPath, _ports);

        Assert.True(File.Exists(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
        Assert.True(File.Exists(Path.Combine(_dataPath, "Macros", "macro.json")));
    }

    [Fact]
    public void Seed_Should_CopyFileUnderItsOwnName_When_SourceIsFile()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");

        DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath, _ports);

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
            _dataPath,
            _ports);

        Assert.Equal("second", File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
    }

    [Fact]
    public void Seed_Should_ThrowListingAllMissingPaths_When_SourcesDoNotExist()
    {
        File.WriteAllText(Path.Combine(_baseDir, "present.json"), "{}");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [
                new DataFileSeed("ghost.json", "ModConfig"),
                new DataFileSeed("present.json", "ModConfig"),
                new DataFileSeed("phantom", "ModConfig"),
            ],
            _baseDir,
            _dataPath,
            _ports));

        // The fixed prefix and the ", "-joined list must both survive: checking each missing
        // path alone cannot tell the joined list from the paths mashed together with no separator.
        Assert.Contains("Data file path(s) not found: ghost.json, phantom", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dataPath, "ModConfig", "present.json")));
    }

    [Fact]
    public void Seed_Should_Throw_When_SeedsIsNull()
        => Assert.Throws<ArgumentNullException>(() => DataSeeder.Seed(null!, _baseDir, _dataPath, _ports));

    [Fact]
    public void Seed_Should_OverwriteAnExistingFile_When_ReSeedingTheSameSingleFile()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "new-bytes");
        Directory.CreateDirectory(Path.Combine(_dataPath, "ModConfig"));
        File.WriteAllText(Path.Combine(_dataPath, "ModConfig", "mymod.json"), "stale-bytes");

        DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath, _ports);

        Assert.Equal("new-bytes", File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "mymod.json")));
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
        DataSeeder.Seed([new DataFileSeed(DataSeeder.WorldSaveFileName, "Saves")], _baseDir, _dataPath, _ports);

        DataSeeder.SeedWorldSave("explicit.vcdbs", _baseDir, _dataPath);

        Assert.Equal(
            "explicit-save",
            File.ReadAllText(Path.Combine(_dataPath, "Saves", DataSeeder.WorldSaveFileName)));
    }

    [Fact]
    public void SeedWorldSave_Should_ThrowNamingThePath_When_FixtureDoesNotExist()
    {
        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => DataSeeder.SeedWorldSave("no-such-world.vcdbs", _baseDir, _dataPath));

        Assert.Contains("no-such-world.vcdbs", ex.Message);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escaped")]
    public void Seed_Should_ThrowSetupException_When_TargetPathEscapesDataPath(string targetPath)
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [new DataFileSeed("mymod.json", targetPath)], _baseDir, _dataPath, _ports));

        Assert.Contains($"Data file target path '{targetPath}' escapes the server data path", ex.Message);
    }

    [Fact]
    public void Seed_Should_ThrowSetupException_When_TargetPathIsRooted()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), "{}");
        string rooted = _root.CreateSubdirectory("elsewhere").FullName;

        Assert.Throws<AtlasSetupException>(() => DataSeeder.Seed(
            [new DataFileSeed("mymod.json", rooted)], _baseDir, _dataPath, _ports));
    }

    [Fact]
    public void Seed_Should_DoNothing_When_NoSeedsAreDeclared()
    {
        DataSeeder.Seed([], _baseDir, _dataPath, _ports);

        Assert.False(Directory.Exists(_dataPath));
    }

    [Fact]
    public void Seed_Should_ResolveAPortToken_When_AFileHoldsOne()
    {
        File.WriteAllText(Path.Combine(_baseDir, "mymod.json"), """{ "port": {{atlas:port:web}} }""");
        var ports = new DataFilePorts();

        DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath, ports);

        string seeded = File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "mymod.json"));
        Assert.Equal($$"""{ "port": {{ports.PortOf("web")}} }""", seeded);
        Assert.InRange(ports.PortOf("web"), 1024, 65535);
    }

    [Fact]
    public void Seed_Should_ResolveTokensInEveryFileOfADirectoryTree_When_SourceIsDirectory()
    {
        string tree = Path.Combine(_baseDir, "serverdata");
        Directory.CreateDirectory(Path.Combine(tree, "ModConfig", "nested"));
        File.WriteAllText(Path.Combine(tree, "ModConfig", "a.json"), "{{atlas:port:a}}");
        File.WriteAllText(Path.Combine(tree, "ModConfig", "nested", "b.yaml"), "port: {{atlas:port:a}}\nadmin: {{atlas:port:b}}\n");
        var ports = new DataFilePorts();

        DataSeeder.Seed([new DataFileSeed("serverdata")], _baseDir, _dataPath, ports);

        string a = ports.PortOf("a").ToString(System.Globalization.CultureInfo.InvariantCulture);
        string b = ports.PortOf("b").ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotEqual(a, b);
        Assert.Equal(a, File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "a.json")));
        Assert.Equal(
            $"port: {a}\nadmin: {b}\n",
            File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "nested", "b.yaml")));
    }

    [Fact]
    public void Seed_Should_GiveOneNameOnePort_When_SeveralSeedsMentionIt()
    {
        File.WriteAllText(Path.Combine(_baseDir, "first.json"), "{{atlas:port:shared}}");
        File.WriteAllText(Path.Combine(_baseDir, "second.json"), "{{atlas:port:shared}}");
        var ports = new DataFilePorts();

        DataSeeder.Seed(
            [new DataFileSeed("first.json", "ModConfig"), new DataFileSeed("second.json", "Other")],
            _baseDir,
            _dataPath,
            ports);

        string port = ports.PortOf("shared").ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(port, File.ReadAllText(Path.Combine(_dataPath, "ModConfig", "first.json")));
        Assert.Equal(port, File.ReadAllText(Path.Combine(_dataPath, "Other", "second.json")));
    }

    [Fact]
    public void Seed_Should_CopyAFileWithoutATokenByteForByte_When_ItIsBinary()
    {
        byte[] bytes = [0, 1, 2, 0xFF, 0xFE, 0x7B, 0x7B, 0xC3, 0x28];
        File.WriteAllBytes(Path.Combine(_baseDir, "blob.bin"), bytes);

        DataSeeder.Seed([new DataFileSeed("blob.bin", "ModConfig")], _baseDir, _dataPath, _ports);

        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dataPath, "ModConfig", "blob.bin")));
    }

    [Fact]
    public void Seed_Should_KeepTheBomAndTheLineEndings_When_ResolvingATokenInAFile()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = System.Text.Encoding.UTF8.GetBytes("{\r\n  \"name\": \"caf\u00e9\",\r\n  \"port\": {{atlas:port:web}}\r\n}\r\n");
        File.WriteAllBytes(Path.Combine(_baseDir, "mymod.json"), [.. bom, .. body]);
        var ports = new DataFilePorts();

        DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath, ports);

        byte[] seeded = File.ReadAllBytes(Path.Combine(_dataPath, "ModConfig", "mymod.json"));
        string expected = $"{{\r\n  \"name\": \"caf\u00e9\",\r\n  \"port\": {ports.PortOf("web")}\r\n}}\r\n";
        Assert.Equal([.. bom, .. System.Text.Encoding.UTF8.GetBytes(expected)], seeded);
    }

    [Fact]
    public void Seed_Should_ThrowNamingTheFile_When_AFileWithATokenIsNotUtf8()
    {
        string file = Path.Combine(_baseDir, "latin1.cfg");
        File.WriteAllBytes(file, [.. System.Text.Encoding.ASCII.GetBytes("{{atlas:port:web}} "), 0xE9]);

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => DataSeeder.Seed([new DataFileSeed("latin1.cfg", "ModConfig")], _baseDir, _dataPath, _ports));

        Assert.Contains(file, ex.Message);
        Assert.Contains("UTF-8", ex.Message);
    }

    [Fact]
    public void Seed_Should_ThrowNamingTheFile_When_ATokenIsMalformed()
    {
        string file = Path.Combine(_baseDir, "mymod.json");
        File.WriteAllText(file, "{{atlas:port:}}");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => DataSeeder.Seed([new DataFileSeed("mymod.json", "ModConfig")], _baseDir, _dataPath, _ports));

        Assert.Contains(file, ex.Message);
    }
}
