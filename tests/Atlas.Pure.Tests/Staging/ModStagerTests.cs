using Atlas.Api;
using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class ModStagerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-stager-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Stage_Should_CopyDllAndZip_When_PathsAreRelative()
    {
        string baseDir = _root.CreateSubdirectory("base").FullName;
        string staging = Path.Combine(_root.FullName, "staging");
        File.WriteAllText(Path.Combine(baseDir, "mod.dll"), "x");
        File.WriteAllText(Path.Combine(baseDir, "mod.zip"), "x");

        ModStager.Stage(new[] { "mod.dll", "mod.zip" }, baseDir, staging);

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

        ModStager.Stage(new[] { "mymod" }, baseDir, staging);

        Assert.True(File.Exists(Path.Combine(staging, "mymod", "modinfo.json")));
        Assert.True(File.Exists(Path.Combine(staging, "mymod", "assets", "a.json")));
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('/')]
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

        var ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage(new[] { root }, baseDir, staging));

        Assert.Contains(root, ex.Message);
    }

    [Fact]
    public void Stage_Should_ThrowSetupExceptionListingAllMissing_When_PathsDoNotExist()
    {
        string baseDir = _root.FullName;
        var ex = Assert.Throws<AtlasSetupException>(
            () => ModStager.Stage(new[] { "ghost.dll", "phantom.zip" }, baseDir, Path.Combine(baseDir, "s")));
        Assert.Contains("ghost.dll", ex.Message);
        Assert.Contains("phantom.zip", ex.Message);
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

        var ex = Assert.Throws<AtlasSetupException>(() => ModStager.StageBridge(source, staging));

        Assert.Contains(source, ex.Message);
        Assert.Contains(Path.Combine(staging, "AtlasBridge.dll"), ex.Message);
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }
}
