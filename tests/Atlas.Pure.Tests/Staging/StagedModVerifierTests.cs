using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
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
    public void VerifyAll_Should_LogVerified_When_TheStagedDllIsTheBuildTheSystemsWereBoundFrom()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        List<string> log = VerifyAll([mod]);

        Assert.Equal(
            $"[Atlas] staged mod 'fakemod.dll': verified (MVID {BoundAssembly.ManifestModule.ModuleVersionId}, loaded from '{BoundAssembly.Location}')",
            Assert.Single(log));
    }

    [Fact]
    public void VerifyAll_Should_SayAnEarlierBootBoundTheCopy_When_ItLivesInASiblingScratchFolder()
    {
        // This boot's scratch folder is one of several under a shared root; the assembly the engine
        // bound sits in another of them, so an earlier boot bound it. The test output folder plays
        // the shared root, and the bound assembly (this test assembly) sits directly in it.
        string output = Path.GetDirectoryName(BoundAssembly.Location)!;
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        string line = Assert.Single(VerifyAll([mod], hostScratch: Path.Combine(output, "this-boot")));

        Assert.EndsWith(
            $"loaded from '{BoundAssembly.Location}', bound by an earlier boot of this process, so that path may be gone)",
            line,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("under-this-boot")]
    [InlineData("elsewhere")]
    public void VerifyAll_Should_AddNoNote_When_TheBoundCopyIsThisBootsOwnOrOutsideTheScratchRoot(string layout)
    {
        // Bound from this boot's own scratch folder (the first boot of the process), or from a
        // folder that is no boot's scratch at all, such as the copy a ProjectReference put next to
        // the test assembly: neither is another boot's, so the path says all there is to say.
        string output = Path.GetDirectoryName(BoundAssembly.Location)!;
        string hostScratch = layout == "under-this-boot"
            ? output
            : Path.Combine(_root.FullName, "scratch", "this-boot");
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        string line = Assert.Single(VerifyAll([mod], hostScratch: hostScratch));

        Assert.EndsWith($"loaded from '{BoundAssembly.Location}')", line, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_LogTheDependencyVerified_When_AStagedFolderShipsALoadedLibraryAsIs()
    {
        // Atlas itself plays the library: loaded in this process, from the test output folder.
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false), ("Atlas.dll", ModAssembly, false));
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());

        List<string> log = VerifyAll([mod]);

        Assert.Equal(2, log.Count);
        Assert.Contains("'mymod': verified (MVID " + BoundAssembly.ManifestModule.ModuleVersionId, log[0], StringComparison.Ordinal);
        Assert.Equal(
            $"[Atlas] staged mod 'mymod': dependency 'Atlas' verified (MVID {ModAssembly.ManifestModule.ModuleVersionId}, loaded from '{ModAssembly.Location}')",
            log[1]);
    }

    [Fact]
    public void VerifyAll_Should_ThrowAtlasSetupException_When_AStagedDependencyIsAnotherBuildThanTheLoadedOne()
    {
        // A stale copy of the library at the folder's root: the mod's own dll matches, so the
        // mod used to read verified while the process ran the other copy of the library.
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false), ("Atlas.dll", ModAssembly, true));
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());
        Guid staleMvid = StagedModVerifier.ReadFile(Path.Combine(folder, "Atlas.dll"))!.Value.Mvid;

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => VerifyAll([mod], new Dictionary<string, string> { [folder] = "/repo/out/mymod" }));

        Assert.Contains("'mymod'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"dependency '{Path.Combine("/repo/out/mymod", "Atlas.dll")}' (MVID {staleMvid})", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            $"another build of that assembly, 'Atlas', loaded from '{ModAssembly.Location}' (MVID {ModAssembly.ManifestModule.ModuleVersionId})",
            ex.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(StagingDir, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_ReadTheStagedZip_When_ADependencyInAZipIsAnotherBuild()
    {
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false), ("Atlas.dll", ModAssembly, true));
        string zip = Path.Combine(StagingDir, "mymod.zip");
        ZipFile.CreateFromDirectory(folder, zip);
        Mod mod = NewMod(EnumModSourceType.ZIP, zip, new FakeSystem());

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => VerifyAll([mod], new Dictionary<string, string> { [zip] = "/repo/out/mymod.zip" }));

        Assert.Contains("dependency '/repo/out/mymod.zip!/Atlas.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_LogTheDependencyAsSkipped_When_NothingOfThatNameIsLoaded()
    {
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false));
        WriteAssembly(Path.Combine(folder, "AtlasUnloadedLibrary.dll"), "AtlasUnloadedLibrary");
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());

        List<string> log = VerifyAll([mod]);

        Assert.Equal(2, log.Count);
        Assert.Equal(
            "[Atlas] staged mod 'mymod': dependency 'AtlasUnloadedLibrary' skipped, not loaded when the world was ready",
            log[1]);
    }

    [Theory]
    [InlineData("Lib")]
    [InlineData("")]
    public void VerifyAll_Should_LeaveTheDependencyAlone_When_TheGameShipsALibraryOfThatName(string subfolder)
    {
        // The game's own copy is bound before any mod folder is looked at, in the real game too,
        // so a stale copy of it in the mod's folder is no finding and gets no line.
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false), ("Atlas.dll", ModAssembly, true));
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());
        string install = Path.Combine(_root.FullName, "install");
        Directory.CreateDirectory(Path.Combine(install, subfolder));
        File.WriteAllText(Path.Combine(install, subfolder, "Atlas.dll"), "the game's copy");

        string line = Assert.Single(VerifyAll([mod], install: install));

        Assert.Contains("'mymod': verified", line, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_LeaveTheDependencyAlone_When_TheLoadedCopyIsTheRuntimesOwn()
    {
        // A NuGet copy of a framework library in the mod's folder: the runtime binds its own.
        Assembly framework = typeof(Enumerable).Assembly;
        string folder = StageFolderMod(("fakemod.dll", BoundAssembly, false), ("System.Linq.dll", framework, true));
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());

        string line = Assert.Single(VerifyAll([mod]));

        Assert.Contains("'mymod': verified", line, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NotLookAtDependencies_When_TheModIsAStagedDll()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        Assert.Single(VerifyAll([mod]));
    }

    [Fact]
    public void VerifyAll_Should_ThrowAtlasSetupException_When_TheStagedDllIsAnotherBuildOfTheBoundAssembly()
    {
        // The same assembly name and version as the loaded one, a different MVID: what a second
        // build of a mod that never bumps its version looks like.
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll([mod]));

        Assert.Contains("'fakemod.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(staged, ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.Location, ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.ManifestModule.ModuleVersionId.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameTheSourcePath_When_TheStagedDllWasCopiedFromAnotherPath()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());
        string source = Path.Combine("/repo/MyMod/bin/Release", "fakemod.dll");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => VerifyAll([mod], new Dictionary<string, string> { [staged] = source }));

        Assert.Contains($"staged from '{source}'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StagingDir, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameTheSourceFolder_When_TheStagedDllIsInsideACopiedFolder()
    {
        string folder = Path.Combine(StagingDir, "mymod");
        Directory.CreateDirectory(folder);
        PatchedCopy(BoundAssembly, Path.Combine(folder, "fakemod.dll"));
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());
        string source = Path.Combine("/repo/out", "mymod");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => VerifyAll([mod], new Dictionary<string, string> { [folder] = source }));

        Assert.Contains($"staged from '{Path.Combine(source, "fakemod.dll")}'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StagingDir, ex.Message, StringComparison.Ordinal);
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

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(mods));

        Assert.Contains("'first.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'second.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_LogOneLinePerStagedMod_When_SomeVerifyAndSomeAreSkipped()
    {
        string code = StageCopyOfTheBoundAssembly(patchMvid: false, "code.dll");
        string content = StageCopyOfTheBoundAssembly(patchMvid: false, "content.dll");
        Mod[] mods =
        [
            NewMod(EnumModSourceType.DLL, code, new FakeSystem()),
            NewMod(EnumModSourceType.DLL, content),
        ];

        List<string> log = VerifyAll(mods);

        Assert.Equal(2, log.Count);
        Assert.Contains("'code.dll': verified", log[0], StringComparison.Ordinal);
        Assert.Contains("'content.dll': skipped", log[1], StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_LogNothing_When_AModWasNotLoadedFromTheStagingDirectory()
    {
        // The bridge and the game's own mods load from elsewhere: not Atlas's to vouch for, and
        // not worth a line per boot.
        string elsewhere = Path.Combine(_root.FullName, "elsewhere", "mod.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        PatchedCopy(BoundAssembly, elsewhere);
        Mod mod = NewMod(EnumModSourceType.DLL, elsewhere, new FakeSystem());

        Assert.Empty(VerifyAll([mod]));
    }

    [Fact]
    public void VerifyAll_Should_LogNotACodeMod_When_ItHasNoSystem()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.DLL, staged);

        Assert.Equal(
            "[Atlas] staged mod 'fakemod.dll': skipped, not a code mod (no ModSystem was loaded from it)",
            Assert.Single(VerifyAll([mod])));
    }

    [Fact]
    public void VerifyAll_Should_LogASourceMod_When_ItIsCompiledFromSource()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true);
        Mod mod = NewMod(EnumModSourceType.CS, staged, new FakeSystem());

        Assert.Equal(
            "[Atlas] staged mod 'fakemod.dll': skipped, no staged dll at its root (a source mod, compiled by the engine)",
            Assert.Single(VerifyAll([mod])));
    }

    [Fact]
    public void VerifyAll_Should_LogASourceMod_When_AStagedFolderShipsNoDll()
    {
        string folder = Path.Combine(StagingDir, "sourcemod");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Mod.cs"), "class Mod {}");
        Mod mod = NewMod(EnumModSourceType.Folder, folder, new FakeSystem());

        Assert.Contains("skipped, no staged dll at its root", Assert.Single(VerifyAll([mod])), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameTheOwner_When_AClassOwnsTheBoot()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());
        List<string> log = [];

        StagedModVerifier.VerifyAll([mod], StagingDir, new Dictionary<string, string>(), log.Add, "My.Scenarios.PlayerScenarios");

        string line = Assert.Single(log);
        Assert.StartsWith("[Atlas] staged mod 'fakemod.dll' for My.Scenarios.PlayerScenarios: verified (MVID ", line, StringComparison.Ordinal);
        Assert.EndsWith($"loaded from '{BoundAssembly.Location}')", line, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_ThrowAtlasSetupException_When_TheEngineRefusedAStagedBuildAndTheModIsAbsent()
    {
        // What a second build of an assembly the process already loaded looks like: the engine
        // logs the same-name refusal, flags the mod, and the mod never reaches the mod list.
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true, "second.dll");
        string source = Path.Combine("/repo/beta", "second.dll");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(
            [],
            new Dictionary<string, string> { [staged] = source },
            [SameNameError(BoundAssembly.GetName().Name!, hint: "secondmod")]));

        Assert.Contains("'secondmod'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"staged from '{source}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.Location, ex.Message, StringComparison.Ordinal);
        Assert.Contains(BoundAssembly.ManifestModule.ModuleVersionId.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StagingDir, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameTheStagedFolder_When_TheRefusedModIsAFolder()
    {
        string folder = Path.Combine(StagingDir, "twin");
        Directory.CreateDirectory(folder);
        PatchedCopy(BoundAssembly, Path.Combine(folder, "twin.dll"));
        string source = Path.Combine("/repo/out", "twin");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(
            [],
            new Dictionary<string, string> { [folder] = source },
            [SameNameError(BoundAssembly.GetName().Name!, hint: null)]));

        Assert.Contains("Mod 'twin'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"staged from '{Path.Combine(source, "twin.dll")}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_ReadTheStagedZip_When_TheRefusedModIsAZip()
    {
        string folder = Path.Combine(_root.FullName, "zipsrc");
        Directory.CreateDirectory(folder);
        PatchedCopy(BoundAssembly, Path.Combine(folder, "twin.dll"));
        Directory.CreateDirectory(StagingDir);
        string zip = Path.Combine(StagingDir, "twin.zip");
        ZipFile.CreateFromDirectory(folder, zip);

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(
            [],
            new Dictionary<string, string> { [zip] = "/repo/out/twin.zip" },
            [SameNameError(BoundAssembly.GetName().Name!)]));

        Assert.Contains("staged from '/repo/out/twin.zip!/twin.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NameEveryRefusedMod_When_SeveralAreAbsent()
    {
        string first = StageCopyOfTheBoundAssembly(patchMvid: true, "first.dll");
        string second = StageCopyOfTheBoundAssembly(patchMvid: true, "second.dll");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(
            [],
            new Dictionary<string, string> { [first] = "/repo/a/first.dll", [second] = "/repo/b/second.dll" },
            [SameNameError(BoundAssembly.GetName().Name!)]));

        Assert.Contains("'/repo/a/first.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'/repo/b/second.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_ThrowOnce_When_AMismatchAndARefusalCoexist()
    {
        string bound = StageCopyOfTheBoundAssembly(patchMvid: true, "bound.dll");
        string refused = StageCopyOfTheBoundAssembly(patchMvid: true, "refused.dll");
        Mod mod = NewMod(EnumModSourceType.DLL, bound, new FakeSystem());

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VerifyAll(
            [mod],
            new Dictionary<string, string> { [bound] = "/repo/a/bound.dll", [refused] = "/repo/b/refused.dll" },
            [SameNameError(BoundAssembly.GetName().Name!)]));

        Assert.Contains("'/repo/a/bound.dll'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'/repo/b/refused.dll'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NotThrow_When_NoEngineErrorSaysItRefusedTheAbsentMod()
    {
        // A staged dll that never made it into the mod list for another reason (no ModSystem,
        // a dependency staged alone) is a deliberately broken mod: not this check's business.
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true, "broken.dll");
        BootDiagnosticEntry other = new(
            EnumLogType.Error, "unknown", "Exception: broken.dll declared as code mod, but there are no .dll files that contain at least one ModSystem", null);

        Assert.Empty(VerifyAll([], new Dictionary<string, string> { [staged] = staged }, [other]));
    }

    [Fact]
    public void VerifyAll_Should_NotThrow_When_TheEngineErrorIsAboutADifferentAssembly()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true, "other.dll");

        Assert.Empty(VerifyAll([], new Dictionary<string, string> { [staged] = staged }, [SameNameError("SomethingElse")]));
    }

    [Fact]
    public void VerifyAll_Should_NotThrow_When_TheStagedModLoadedDespiteTheSameNameError()
    {
        // The error can belong to another file: a mod that is in the list loaded, whatever else
        // the engine said about an assembly of that name.
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false);
        Mod mod = NewMod(EnumModSourceType.DLL, staged, new FakeSystem());

        List<string> log = VerifyAll(
            [mod], new Dictionary<string, string> { [staged] = staged }, [SameNameError(BoundAssembly.GetName().Name!)]);

        Assert.Contains("verified", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAll_Should_NotThrow_When_TheAbsentStagedBuildIsTheLoadedOne()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: false, "same.dll");

        Assert.Empty(VerifyAll(
            [], new Dictionary<string, string> { [staged] = staged }, [SameNameError(BoundAssembly.GetName().Name!)]));
    }

    [Fact]
    public void VerifyAll_Should_NotLookForRefusals_When_NoEngineErrorsWerePassed()
    {
        string staged = StageCopyOfTheBoundAssembly(patchMvid: true, "second.dll");

        Assert.Empty(VerifyAll([], new Dictionary<string, string> { [staged] = staged }, engineErrors: null));
    }

    [Fact]
    public void VerifyAll_Should_NotLookForRefusals_When_TheStagedPathIsOutsideTheStagingDirectory()
    {
        string elsewhere = Path.Combine(_root.FullName, "elsewhere", "second.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        PatchedCopy(BoundAssembly, elsewhere);

        Assert.Empty(VerifyAll(
            [], new Dictionary<string, string> { [elsewhere] = elsewhere }, [SameNameError(BoundAssembly.GetName().Name!)]));
    }

    [Fact]
    public void ReadStaged_Should_ReturnNothing_When_TheModIsCompiledFromSource()
        => Assert.Empty(StagedModVerifier.ReadStaged(EnumModSourceType.CS, Path.Combine(_root.FullName, "mod.cs")));

    private List<string> VerifyAll(
        Mod[] mods,
        IReadOnlyDictionary<string, string>? sources = null,
        IReadOnlyList<BootDiagnosticEntry>? engineErrors = null,
        string? hostScratch = null,
        string? install = null)
    {
        List<string> log = [];
        StagedModVerifier.VerifyAll(
            mods,
            StagingDir,
            sources ?? new Dictionary<string, string>(),
            log.Add,
            engineErrors: engineErrors,
            hostScratch: hostScratch,
            install: install);
        return log;
    }

    // A staged folder mod "mymod" holding these dlls, each a copy of an assembly (with another MVID
    // when asked): the mod's own dll first, then its libraries.
    private string StageFolderMod(params (string FileName, Assembly Source, bool PatchMvid)[] files)
    {
        string folder = Path.Combine(StagingDir, "mymod");
        Directory.CreateDirectory(folder);
        foreach ((string fileName, Assembly source, bool patchMvid) in files)
        {
            PatchedCopy(source, Path.Combine(folder, fileName), patchMvid);
        }

        return folder;
    }

    // A managed assembly nothing in this process has loaded, written under its own name.
    private static void WriteAssembly(string path, string name)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        builder.DefineDynamicModule(name);
        builder.Save(path);
    }

    // What the engine logs for a second build of an assembly the process already loaded (see
    // StagedModBinding.DescribeRefusal).
    private static BootDiagnosticEntry SameNameError(string assemblyName, string? hint = null)
    {
        string message =
            $"Exception: Could not load file or assembly '{assemblyName}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. " +
            "Assembly with same name is already loaded";
        return new BootDiagnosticEntry(EnumLogType.Error, "unknown", message, null, hint);
    }

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
