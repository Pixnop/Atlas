using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Atlas.Api;
using Atlas.Engine.Tests.Support;
using DependencyLibraryFixture;

namespace Atlas.Engine.Tests;

/// <summary>Covers what the staged-mod check says about a folder mod that ships a library next to
/// its own dll, against DependentFixtureMod (a mod that uses DependencyLibraryFixture at
/// start-up). The mod's dll is not next to this assembly, so the engine binds the first copy a
/// host stages, which is what makes the "bound by an earlier boot" note reachable: every boot
/// after the first in the process binds that copy, not its own. The library is referenced by this
/// suite, so the copy next to this assembly is the one the runtime binds for it, whatever a staged
/// folder holds beside the mod: a staged copy that is another build is what the check must name.</summary>
[Trait("Category", "E2E")]
public sealed class StagedModDependencyTests : IDisposable
{
    private const string ModDll = "DependentFixtureMod.dll";
    private const string LibraryDll = "DependencyLibraryFixture.dll";

    private static readonly string BuiltMod = Path.Combine(TestPaths.OwnOutputDirectory, "dependent-mod", ModDll);

    // The library build the runtime binds: the copy next to this assembly.
    private static readonly string BoundLibrary = typeof(SharedType).Assembly.Location;

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("atlas-dependent-");

    private enum LibraryCopy
    {
        None,
        Identical,
        OtherBuild,
    }

    public void Dispose()
    {
        _work.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StartAsync_Should_SayAnEarlierBootBoundTheCopy_When_ASecondHostStagesTheSameMod()
    {
        string folder = MakeFolderMod();

        // Whichever test in this process stages the mod first binds it. Boot once so that is
        // certainly over, then boot again: the second boot's own staged copy is not the one in use.
        await using (ServerHost first = TestHosts.New(folder))
        {
            await first.StartAsync();
        }

        await using ServerHost second = TestHosts.New(folder);
        string stderr = await Stderr.CaptureAsync(() => second.StartAsync());

        string line = Assert.Single(StagedModLines(stderr));
        Assert.StartsWith("[Atlas] staged mod 'dependentfixture': verified (MVID ", line, StringComparison.Ordinal);
        Assert.EndsWith(", bound by an earlier boot of this process, so that path may be gone)", line, StringComparison.Ordinal);
        Assert.DoesNotContain(second.DataPath, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_LogTheLibraryVerified_When_TheStagedCopyIsTheBuildTheEngineLoaded()
    {
        string folder = MakeFolderMod(library: LibraryCopy.Identical);
        await using ServerHost host = TestHosts.New(folder);

        string stderr = await Stderr.CaptureAsync(() => host.StartAsync());

        string[] lines = StagedModLines(stderr);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("[Atlas] staged mod 'dependentfixture': verified (MVID ", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            $"[Atlas] staged mod 'dependentfixture': dependency 'DependencyLibraryFixture' verified (MVID {ReadMvid(BoundLibrary)}, loaded from '{BoundLibrary}')",
            lines[1]);
    }

    [Fact]
    public async Task StartAsync_Should_ThrowAtlasSetupException_When_AStagedLibraryIsAnotherBuildThanTheLoadedOne()
    {
        // The mod's own dll matches whichever build the engine bound, so the mod read verified;
        // the stale library beside it was never looked at, and the process ran the other copy.
        string folder = MakeFolderMod(library: LibraryCopy.OtherBuild);
        await using ServerHost host = TestHosts.New(folder);

        AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(() => host.StartAsync());

        Assert.Contains("'dependentfixture'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"dependency '{Path.Combine(folder, LibraryDll)}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            $"another build of that assembly, 'DependencyLibraryFixture', loaded from '{BoundLibrary}' (MVID {ReadMvid(BoundLibrary)})",
            ex.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(host.DataPath, "TestMods"), ex.Message, StringComparison.Ordinal);
    }

    private static string[] StagedModLines(string stderr)
        => [.. stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("[Atlas] staged mod ", StringComparison.Ordinal))];

    // The library's MVID, read straight from the file's metadata: an oracle that shares no code with
    // the check under test.
    private static Guid ReadMvid(string dll)
    {
        using var pe = new PEReader(File.OpenRead(dll));
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    /// <summary>Lays the fixture mod out as a folder mod: its dll, the <c>modinfo.json</c> the game
    /// reads for a folder, and the library at the root when asked, as the build output of a mod
    /// that uses one lays it out.</summary>
    private string MakeFolderMod(LibraryCopy library = LibraryCopy.None)
    {
        string folder = Path.Combine(Directory.CreateDirectory(Path.Combine(_work.FullName, "mod")).FullName, "dependentfixture");
        Directory.CreateDirectory(folder);
        File.Copy(BuiltMod, Path.Combine(folder, ModDll));
        File.WriteAllText(
            Path.Combine(folder, "modinfo.json"),
            """{ "type": "code", "modid": "dependentfixture", "name": "Atlas Dependent Fixture", "version": "0.1.0", "side": "Server" }""");
        if (library != LibraryCopy.None)
        {
            byte[] image = File.ReadAllBytes(BoundLibrary);
            if (library == LibraryCopy.OtherBuild)
            {
                // The GUID heap holds the MVID verbatim: rewriting it keeps the library's name and
                // version and makes the copy another build of it.
                byte[] mvid = ReadMvid(BoundLibrary).ToByteArray();
                int at = image.AsSpan().IndexOf(mvid);
                Assert.True(at >= 0, "the MVID is stored verbatim in the file");
                Guid.NewGuid().ToByteArray().CopyTo(image, at);
            }

            File.WriteAllBytes(Path.Combine(folder, LibraryDll), image);
        }

        return folder;
    }
}
