using Atlas.Api;
using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class ModStagerTests : IDisposable
{
    private static readonly string[] DllAndZipMods = ["mod.dll", "mod.zip"];
    private static readonly string[] FolderMod = ["mymod"];
    private static readonly string[] MissingMods = ["ghost.dll", "phantom.zip"];
    private static readonly string[] SingleDllMod = ["mod.dll"];

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-stager-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Stage_Should_CopyDllAndZip_When_PathsAreRelative()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string staging = Path.Combine(_root.FullName, "staging");
        File.WriteAllText(Path.Combine(baseDir, "mod.dll"), "x");
        File.WriteAllText(Path.Combine(baseDir, "mod.zip"), "x");

        ModStager.Stage(DllAndZipMods, baseDir, staging);

        Assert.True(File.Exists(Path.Combine(staging, "mod.dll")));
        Assert.True(File.Exists(Path.Combine(staging, "mod.zip")));
    }

    [Fact]
    public void Stage_Should_CopyFolderModRecursively_When_PathIsDirectory()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string modDir = Path.Combine(baseDir, "mymod");
        Directory.CreateDirectory(Path.Combine(modDir, "assets"));
        File.WriteAllText(Path.Combine(modDir, "modinfo.json"), "{}");
        File.WriteAllText(Path.Combine(modDir, "assets", "a.json"), "{}");
        string staging = Path.Combine(_root.FullName, "staging");

        ModStager.Stage(FolderMod, baseDir, staging);

        Assert.True(File.Exists(Path.Combine(staging, "mymod", "modinfo.json")));
        Assert.True(File.Exists(Path.Combine(staging, "mymod", "assets", "a.json")));
    }

    /// <summary>On Linux a backslash is a legal file-name character, not a separator, so the
    /// backslash case only makes sense on Windows.</summary>
    public static TheoryData<char> TrailingSeparators()
    {
        var separators = new TheoryData<char> { '/' };
        if (OperatingSystem.IsWindows())
        {
            separators.Add('\\');
        }

        return separators;
    }

    [Theory]
    [MemberData(nameof(TrailingSeparators))]
    public void Stage_Should_StageIntoNamedSubfolder_When_DirectoryPathHasTrailingSeparator(char separator)
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string modDir = Path.Combine(baseDir, "mymod");
        Directory.CreateDirectory(Path.Combine(modDir, "assets"));
        File.WriteAllText(Path.Combine(modDir, "modinfo.json"), "{}");
        File.WriteAllText(Path.Combine(modDir, "assets", "a.json"), "{}");
        string staging = Path.Combine(_root.FullName, "staging");
        string trailing = modDir + separator;

        ModStager.Stage(new[] { trailing }, baseDir, staging);

        Assert.True(File.Exists(Path.Combine(staging, "mymod", "modinfo.json")));
        Assert.True(File.Exists(Path.Combine(staging, "mymod", "assets", "a.json")));

        // The folder's own contents must not have been flattened straight into the staging root.
        Assert.False(File.Exists(Path.Combine(staging, "modinfo.json")));
    }

    [Fact]
    public void Stage_Should_ThrowAtlasSetupException_When_PathYieldsEmptyStagingName()
    {
        string baseDir = _root.FullName;
        string staging = Path.Combine(_root.FullName, "staging");

        // A bare root (e.g. "C:\") has no file/directory name component: GetFileName returns "".
        string root = Path.GetPathRoot(baseDir)!;

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage(new[] { root }, baseDir, staging));

        Assert.Contains("Could not derive a staging folder name from mod path", ex.Message);
        Assert.Contains(root, ex.Message);
    }

    [Fact]
    public void Stage_Should_ThrowSetupExceptionListingAllMissing_When_PathsDoNotExist()
    {
        string baseDir = _root.FullName;
        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage(MissingMods, baseDir, Path.Combine(baseDir, "s")));

        // Both the fixed prefix and the ", "-joined list must survive: a Contains check on each
        // path alone cannot tell the joined list from two paths mashed together with no separator.
        Assert.Contains("Mod path(s) not found: ghost.dll, phantom.zip", ex.Message);
    }

    [Fact]
    public void Stage_Should_ThrowSetupExceptionNamingBothSources_When_TwoFoldersShareAName()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string first = Path.Combine(baseDir, "alpha", "net10.0");
        string second = Path.Combine(baseDir, "beta", "net10.0");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "Alpha.dll"), "x");
        File.WriteAllText(Path.Combine(second, "Beta.dll"), "x");
        string staging = Path.Combine(_root.FullName, "staging");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage([first, second], baseDir, staging));

        Assert.Contains("'net10.0'", ex.Message);
        Assert.Contains($"'{first}'", ex.Message);
        Assert.Contains($"'{second}'", ex.Message);
        Assert.Contains("Give each mod its own folder name", ex.Message);

        // Nothing was merged before the failure: the check runs ahead of any copy.
        Assert.False(Directory.Exists(Path.Combine(staging, "net10.0")));
    }

    [Fact]
    public void Stage_Should_ThrowSetupExceptionNamingBothSources_When_TwoDllsShareAName()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string first = Path.Combine(baseDir, "alpha", "mod.dll");
        string second = Path.Combine(baseDir, "beta", "mod.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, "first-bytes");
        File.WriteAllText(second, "second-bytes");
        string staging = Path.Combine(_root.FullName, "staging");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage([first, second], baseDir, staging));

        Assert.Contains("'mod.dll'", ex.Message);
        Assert.Contains($"'{first}'", ex.Message);
        Assert.Contains($"'{second}'", ex.Message);
        Assert.False(File.Exists(Path.Combine(staging, "mod.dll")));
    }

    [Fact]
    public void Stage_Should_StageOnce_When_TheSameSourceIsListedTwice()
    {
        // The same mod reaching Atlas from both [AtlasMods] and the generated manifest, one of the
        // two with a trailing separator, is one mod, not a collision.
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string modDir = Path.Combine(baseDir, "mymod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "modinfo.json"), "{}");
        string staging = Path.Combine(_root.FullName, "staging");

        IReadOnlyDictionary<string, string> sources = ModStager.Stage([modDir, modDir + "/"], baseDir, staging);

        Assert.Equal(modDir, Assert.Single(sources).Value);
        Assert.True(File.Exists(Path.Combine(staging, "mymod", "modinfo.json")));
    }

    [Fact]
    public void Stage_Should_StageBoth_When_TwoFoldersHaveDistinctNames()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string staging = Path.Combine(_root.FullName, "staging");
        foreach (string name in new[] { "alpha", "beta" })
        {
            string dir = Path.Combine(baseDir, "atlas-mods", name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "modinfo.json"), "{}");
        }

        IReadOnlyDictionary<string, string> sources = ModStager.Stage(
            [Path.Combine("atlas-mods", "alpha"), Path.Combine("atlas-mods", "beta")], baseDir, staging);

        Assert.Equal(2, sources.Count);
        Assert.True(File.Exists(Path.Combine(staging, "alpha", "modinfo.json")));
        Assert.True(File.Exists(Path.Combine(staging, "beta", "modinfo.json")));
    }

    [Fact]
    public void Stage_Should_Throw_When_ModPathsIsNull()
        => Assert.Throws<ArgumentNullException>(
            () => ModStager.Stage(null!, _root.FullName, Path.Combine(_root.FullName, "staging")));

    [Fact]
    public void Stage_Should_OverwriteAnExistingFile_When_ReStagingTheSameMod()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string staging = Path.Combine(_root.FullName, "staging");
        File.WriteAllText(Path.Combine(baseDir, "mod.dll"), "new-bytes");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "mod.dll"), "stale-bytes");

        ModStager.Stage(SingleDllMod, baseDir, staging);

        Assert.Equal("new-bytes", File.ReadAllText(Path.Combine(staging, "mod.dll")));
    }

    [Fact]
    public void Stage_Should_MapEachStagedPathToTheSourceItWasCopiedFrom_When_ModsAreStaged()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string staging = Path.Combine(_root.FullName, "staging");
        File.WriteAllText(Path.Combine(baseDir, "mod.dll"), "x");
        File.WriteAllText(Path.Combine(baseDir, "mod.zip"), "x");
        Directory.CreateDirectory(Path.Combine(baseDir, "mymod"));
        File.WriteAllText(Path.Combine(baseDir, "mymod", "modinfo.json"), "{}");

        IReadOnlyDictionary<string, string> sources = ModStager.Stage(["mod.dll", "mod.zip", "mymod"], baseDir, staging);

        Assert.Equal(3, sources.Count);
        Assert.Equal(Path.Combine(baseDir, "mod.dll"), sources[Path.Combine(staging, "mod.dll")]);
        Assert.Equal(Path.Combine(baseDir, "mod.zip"), sources[Path.Combine(staging, "mod.zip")]);
        Assert.Equal(Path.Combine(baseDir, "mymod"), sources[Path.Combine(staging, "mymod")]);
    }

    [Fact]
    public void Stage_Should_MapToTheTrimmedSource_When_DirectoryPathHasTrailingSeparator()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string modDir = Path.Combine(baseDir, "mymod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "modinfo.json"), "{}");
        string staging = Path.Combine(_root.FullName, "staging");

        IReadOnlyDictionary<string, string> sources = ModStager.Stage([modDir + "/"], baseDir, staging);

        KeyValuePair<string, string> entry = Assert.Single(sources);
        Assert.Equal(Path.Combine(staging, "mymod"), entry.Key);
        Assert.Equal(modDir, entry.Value);
    }

    [Fact]
    public void Stage_Should_MapToTheSourceResolvedAgainstTheBaseDirectory_When_PathsAreRelative()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        Directory.CreateDirectory(Path.Combine(baseDir, "out"));
        File.WriteAllText(Path.Combine(baseDir, "out", "mod.dll"), "x");
        string staging = Path.Combine(_root.FullName, "staging");

        IReadOnlyDictionary<string, string> sources = ModStager.Stage([Path.Combine("out", "..", "out", "mod.dll")], baseDir, staging);

        Assert.Equal(Path.Combine(baseDir, "out", "mod.dll"), Assert.Single(sources).Value);
    }

    [Fact]
    public void StageBridge_Should_CopyAssemblyIntoCreatedFolder_When_SourceExists()
    {
        string source = Path.Combine(_root.FullName, "AtlasBridge.dll");
        File.WriteAllText(source, "x");
        string staging = Path.Combine(_root.FullName, "BridgeMod");

        ModStager.StageBridge(source, staging);

        Assert.True(File.Exists(Path.Combine(staging, "AtlasBridge.dll")));
    }

    [Fact]
    public void StageBridge_Should_ThrowSetupExceptionNamingBothPaths_When_CopyFails()
    {
        string source = Path.Combine(_root.FullName, "missing", "AtlasBridge.dll");
        string staging = Path.Combine(_root.FullName, "BridgeMod");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => ModStager.StageBridge(source, staging));

        Assert.Contains(source, ex.Message);
        Assert.Contains(Path.Combine(staging, "AtlasBridge.dll"), ex.Message);
        Assert.IsType<IOException>(ex.InnerException, exactMatch: false);
    }

    [Fact]
    public void StageBridge_Should_ThrowAtlasSetupException_When_PathYieldsEmptyStagingName()
    {
        // A bare separator trims to an empty name on every platform.
        string staging = Path.Combine(_root.FullName, "BridgeMod");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => ModStager.StageBridge("/", staging));

        Assert.Contains("Could not derive a staging file name from bridge path '/'", ex.Message);
        Assert.Contains("no file name component", ex.Message);
    }

    [Fact]
    public void StageBridge_Should_OverwriteAnExistingFile_When_ReStagingTheSameBridge()
    {
        string source = Path.Combine(_root.FullName, "AtlasBridge.dll");
        File.WriteAllText(source, "new-bytes");
        string staging = Path.Combine(_root.FullName, "BridgeMod");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "AtlasBridge.dll"), "stale-bytes");

        ModStager.StageBridge(source, staging);

        Assert.Equal("new-bytes", File.ReadAllText(Path.Combine(staging, "AtlasBridge.dll")));
    }
}
