using System.IO.Compression;
using System.Reflection;
using Atlas.Api;
using Atlas.Internal.Staging;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Staging;

public class StagedModVerifierTests : IDisposable
{
    // Two real, managed, differently named assemblies this suite always has on disk: Atlas itself
    // and this test assembly, which is also where the fake mod's system lives.
    private static readonly Assembly ModAssembly = typeof(StagedModVerifier).Assembly;
    private static readonly Assembly DependencyAssembly = typeof(StagedModVerifierTests).Assembly;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-verifier-");

    private static Assembly BoundAssembly => typeof(FakeSystem).Assembly;

    private string StagingDir => Path.Combine(_root.FullName, "TestMods");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ReadFile_Should_ReturnTheNameAndMvidOfTheLoadedModule_When_ReadingAnAssemblyFromDisk()
    {
        StagedModBinding.AssemblyFile? file = StagedModVerifier.ReadFile(ModAssembly.Location);

        Assert.NotNull(file);
        Assert.Equal(ModAssembly.GetName().Name, file.Value.SimpleName);
        Assert.Equal(ModAssembly.ManifestModule.ModuleVersionId, file.Value.Mvid);
        Assert.Equal(ModAssembly.Location, file.Value.Path);
    }

    [Fact]
    public void ReadFile_Should_ReturnTheSameMvid_When_TheFileIsAByteIdenticalCopyAtAnotherPath()
    {
        string copy = Path.Combine(_root.FullName, "elsewhere", "Renamed.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(ModAssembly.Location, copy);

        StagedModBinding.AssemblyFile? file = StagedModVerifier.ReadFile(copy);

        Assert.NotNull(file);
        Assert.Equal(ModAssembly.ManifestModule.ModuleVersionId, file.Value.Mvid);
        Assert.Equal(ModAssembly.GetName().Name, file.Value.SimpleName);
    }

    [Fact]
    public void ReadFile_Should_NotLoadTheAssembly_When_ReadingIt()
    {
        string copy = Path.Combine(_root.FullName, "unloaded.dll");
        File.Copy(ModAssembly.Location, copy);

        Assert.NotNull(StagedModVerifier.ReadFile(copy));

        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            a => !a.IsDynamic && string.Equals(a.Location, copy, StringComparison.Ordinal));
    }

    [Fact]
    public void ReadFile_Should_ReturnNull_When_TheFileIsMissing()
        => Assert.Null(StagedModVerifier.ReadFile(Path.Combine(_root.FullName, "ghost.dll")));

    [Fact]
    public void ReadFile_Should_ReturnNull_When_TheFileIsNotAManagedAssembly()
    {
        string notAssembly = Path.Combine(_root.FullName, "native.dll");
        File.WriteAllText(notAssembly, "not an assembly");

        Assert.Null(StagedModVerifier.ReadFile(notAssembly));
    }

    [Fact]
    public void ReadFile_Should_ReturnNull_When_TheFileIsEmpty()
    {
        string empty = Path.Combine(_root.FullName, "empty.dll");
        File.WriteAllBytes(empty, []);

        Assert.Null(StagedModVerifier.ReadFile(empty));
    }

    [Fact]
    public void ReadStaged_Should_ReadTheFileItself_When_TheModIsAStagedDll()
    {
        IReadOnlyList<StagedModBinding.AssemblyFile> files =
            StagedModVerifier.ReadStaged(EnumModSourceType.DLL, ModAssembly.Location);

        StagedModBinding.AssemblyFile file = Assert.Single(files);
        Assert.Equal(ModAssembly.ManifestModule.ModuleVersionId, file.Mvid);
    }

    [Fact]
    public void ReadStaged_Should_ReadOnlyTheRootDlls_When_TheModIsAStagedFolder()
    {
        string folder = Path.Combine(_root.FullName, "mod");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.Copy(ModAssembly.Location, Path.Combine(folder, "Mod.dll"));
        File.Copy(DependencyAssembly.Location, Path.Combine(folder, "Dependency.dll"));
        File.Copy(ModAssembly.Location, Path.Combine(folder, "sub", "Nested.dll"));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a dll");
        File.WriteAllText(Path.Combine(folder, "Broken.dll"), "not an assembly");

        IReadOnlyList<StagedModBinding.AssemblyFile> files =
            StagedModVerifier.ReadStaged(EnumModSourceType.Folder, folder);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.SimpleName == ModAssembly.GetName().Name && f.Path == Path.Combine(folder, "Mod.dll"));
        Assert.Contains(files, f => f.SimpleName == DependencyAssembly.GetName().Name);
    }

    [Fact]
    public void ReadStaged_Should_InflateOnlyTheRootDlls_When_TheModIsAStagedZip()
    {
        string zipPath = Path.Combine(_root.FullName, "mod.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(ModAssembly.Location, "Mod.dll");
            zip.CreateEntryFromFile(DependencyAssembly.Location, "Dependency.dll");
            zip.CreateEntryFromFile(ModAssembly.Location, "native/Nested.dll");
            zip.CreateEntryFromFile(ModAssembly.Location, "notes.txt");
            ZipArchiveEntry broken = zip.CreateEntry("Broken.dll");
            using var writer = new StreamWriter(broken.Open());
            writer.Write("not an assembly");
        }

        IReadOnlyList<StagedModBinding.AssemblyFile> files =
            StagedModVerifier.ReadStaged(EnumModSourceType.ZIP, zipPath);

        Assert.Equal(2, files.Count);
        StagedModBinding.AssemblyFile mod = Assert.Single(files, f => f.SimpleName == ModAssembly.GetName().Name);
        Assert.Equal(ModAssembly.ManifestModule.ModuleVersionId, mod.Mvid);
        Assert.Equal(zipPath + "!/Mod.dll", mod.Path);
    }

    [Fact]
    public void ReadStaged_Should_ReturnNothing_When_TheZipCannotBeRead()
    {
        string notZip = Path.Combine(_root.FullName, "broken.zip");
        File.WriteAllText(notZip, "not a zip");

        Assert.Empty(StagedModVerifier.ReadStaged(EnumModSourceType.ZIP, notZip));
    }

    [Fact]
    public void ReadStaged_Should_ReturnNothing_When_TheFolderIsGone()
        => Assert.Empty(StagedModVerifier.ReadStaged(EnumModSourceType.Folder, Path.Combine(_root.FullName, "gone")));

    [Fact]
    public void VerifyAll_Should_NotThrow_When_TheStagedDllIsTheBuildTheSystemsWereBoundFrom()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        Assert.Null(Record.Exception(() => StagedModVerifier.VerifyAll([mod], StagingDir)));
    }

    [Fact]
    public void VerifyAll_Should_ThrowAtlasSetupException_When_TheStagedDllIsAnotherBuildOfTheBoundAssembly()
    {
        // The same assembly name and version as the loaded one, a different MVID: what a second
        // build of a mod that never bumps its version looks like.
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => StagedModVerifier.VerifyAll([mod], StagingDir));

        Assert.Contains("'fakemod.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(staged, ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.Location, ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.ManifestModule.ModuleVersionId.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameEveryMismatchedMod_When_SeveralAreStaged()
    {
        string first = StageCopyOfTheBoundAssembly(patchMvid: true, "first.dll");
        string second = StageCopyOfTheBoundAssembly(patchMvid: true, "second.dll");
        Mod[] mods =
        [
            NewMod(EnumModSourceType.DLL, first, new FakeSystem()),
            NewMod(EnumModSourceType.DLL, second, new FakeSystem()),
        ];

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => StagedModVerifier.VerifyAll(mods, StagingDir));

        Assert.Contains("'first.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'second.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_SkipAMod_When_ItWasNotLoadedFromTheStagingDirectory()
    {
        // The bridge and the game's own mods load from elsewhere: not Atlas's to vouch for.
        string elsewhere = Path.Combine(_root.FullName, "elsewhere", "mod.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        PatchedCopy(BoundAssembly, elsewhere);
        Mod mod = NewMod(EnumModSourceType.DLL, elsewhere, new FakeSystem());

        Assert.Null(Record.Exception(() => StagedModVerifier.VerifyAll([mod], StagingDir)));
    }

    [Fact]
    public void VerifyAll_Should_SkipAMod_When_ItHasNoSystem()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.DLL, staged);

        Assert.Null(Record.Exception(() => StagedModVerifier.VerifyAll([mod], StagingDir)));
    }

    [Fact]
    public void VerifyAll_Should_SkipAMod_When_ItIsCompiledFromSource()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.CS, staged, new FakeSystem());

        Assert.Null(Record.Exception(() => StagedModVerifier.VerifyAll([mod], StagingDir)));
    }

    [Fact]
    public void ReadStaged_Should_ReturnNothing_When_TheModIsCompiledFromSource()
        => Assert.Empty(StagedModVerifier.ReadStaged(EnumModSourceType.CS, Path.Combine(_root.FullName, "mod.cs")));

    /// <summary>Copies an assembly's file, optionally rewriting its MVID in place: the GUID heap
    /// holds the 16 bytes verbatim, so the copy keeps the assembly's name and version and becomes
    /// another build of it.</summary>
    private static void PatchedCopy(Assembly assembly, string destination, bool patchMvid = true)
    {
        byte[] image = File.ReadAllBytes(assembly.Location);
        if (patchMvid)
        {
            byte[] mvid = assembly.ManifestModule.ModuleVersionId.ToByteArray();
            int at = image.AsSpan().IndexOf(mvid);
            Assert.True(at >= 0, "the MVID is stored verbatim in the file");
            Guid.NewGuid().ToByteArray().CopyTo(image, at);
        }

        File.WriteAllBytes(destination, image);
    }

    private string StageCopyOfTheBoundAssembly(bool patchMvid, string fileName = "fakemod.dll")
    {
        Directory.CreateDirectory(StagingDir);
        string staged = Path.Combine(StagingDir, fileName);
        PatchedCopy(BoundAssembly, staged, patchMvid);
        return staged;
    }

    // Mod's members are set by the engine (internal setters) and the check reads them, so a test
    // sets them the way it otherwise cannot. Info stays null, so the mod is named by its file.
    private static Mod NewMod(EnumModSourceType type, string sourcePath, ModSystem? system = null)
    {
        var mod = new FakeMod();
        SetMember(mod, nameof(Mod.SourceType), type);
        SetMember(mod, nameof(Mod.SourcePath), sourcePath);
        SetMember(mod, nameof(Mod.FileName), Path.GetFileName(sourcePath));
        SetMember(mod, nameof(Mod.Systems), system == null ? new List<ModSystem>() : new List<ModSystem> { system });
        return mod;
    }

    private static void SetMember(Mod mod, string property, object value)
        => typeof(Mod).GetProperty(property)!.SetValue(mod, value);

    private sealed class FakeMod : Mod
    {
    }

    // Lives in this test assembly, so this is the assembly a fake mod's systems are bound from,
    // and the one the staged copies above are made of.
    private sealed class FakeSystem : ModSystem
    {
    }
}
