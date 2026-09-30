using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Atlas.Api;
using Atlas.Engine.Tests.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers the staged-mod binding check end to end (issue #170), against
/// BindingFixtureMod: two builds of ONE mod identity (same AssemblyName, same frozen
/// AssemblyVersion) that differ only in what the mod reports. The "alpha" build is
/// ProjectReferenced by this suite, so its dll sits next to this assembly and the default
/// binding hands it to the engine for any staged dll of that identity; the "beta" build is the
/// other one, staged from binding-beta/. Staging beta therefore used to boot green on alpha,
/// silently; the check turns that into a setup error naming both files. Every shape a mod can
/// be staged in (dll, folder, zip) is covered, since each reads its staged dll differently.</summary>
[Trait("Category", "E2E")]
public sealed class StagedModBindingTests : IDisposable
{
    private const string ModDll = "BindingFixtureMod.dll";
    private const string SystemName = "BindingFixtureMod.BindingFixtureModSystem";

    // The build next to this assembly, the one the engine binds: what a ProjectReference to the
    // mod puts in the output.
    private static readonly string ReferencedBuild = Path.Combine(TestPaths.OwnOutputDirectory, ModDll);

    // The other build of the same identity.
    private static readonly string OtherBuild = Path.Combine(TestPaths.OwnOutputDirectory, "binding-beta", ModDll);

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("atlas-binding-");

    public static TheoryData<string> Shapes() => new() { "dll", "folder", "zip" };

    public void Dispose()
    {
        _work.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task StartAsync_Should_ThrowAtlasSetupException_When_TheStagedBuildIsNotTheOneTheEngineBound(string shape)
    {
        string source = MakeMod(shape, OtherBuild);
        await using ServerHost host = TestHosts.New(source);

        AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(() => host.StartAsync());

        string stagedDll = StagedDllPath(host, shape, source);
        Assert.Contains("bindingfixture", ex.Message, StringComparison.Ordinal);
        Assert.Contains(stagedDll, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ReadMvid(OtherBuild).ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains(ReferencedBuild, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ReadMvid(ReferencedBuild).ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains("one build", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task StartAsync_Should_Boot_When_TheStagedBuildIsTheOneTheEngineBound(string shape)
    {
        // The staged file is a byte-identical copy of the bound build at another path: what
        // differs between the two is the path, never the content, and only content counts.
        string source = MakeMod(shape, ReferencedBuild);
        await using ServerHost host = TestHosts.New(source);
        await host.StartAsync();

        string? running = null;
        await host.RunScenarioAsync(world =>
        {
            running = RunningBuild(world.Api);
            return Task.CompletedTask;
        });

        Assert.Equal("alpha", running);
    }

    [Fact]
    public async Task StartAsync_Should_Boot_When_TheStagedFileIsTheReferencedBuildItself()
    {
        // The ordinary layout of a mod's own tests: a ProjectReference to the mod, and that same
        // build (the dll in the output) staged.
        await using ServerHost host = TestHosts.New(ReferencedBuild);
        await host.StartAsync();

        string? running = null;
        await host.RunScenarioAsync(world =>
        {
            running = RunningBuild(world.Api);
            return Task.CompletedTask;
        });

        Assert.Equal("alpha", running);
    }

    private static string? RunningBuild(ICoreServerAPI api)
    {
        // Read off the loaded instance by reflection: the loaded type is whichever assembly the
        // engine bound, so a compile-time reference would say nothing about it.
        ModSystem system = api.ModLoader.GetModSystem(SystemName);
        return (string?)system.GetType().GetProperty("Build")!.GetValue(system);
    }

    // The mod dll's MVID, read straight from the file's metadata: an oracle that shares no code
    // with the check under test.
    private static Guid ReadMvid(string dll)
    {
        using var pe = new PEReader(File.OpenRead(dll));
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    private static string StagedDllPath(ServerHost host, string shape, string source)
    {
        string staged = Path.Combine(host.DataPath, "TestMods", Path.GetFileName(source));
        return shape switch
        {
            "dll" => staged,
            "folder" => Path.Combine(staged, ModDll),
            _ => staged + "!/" + ModDll,
        };
    }

    /// <summary>Lays <paramref name="dll"/> out as a staged mod of the given shape and returns
    /// the path to hand to the host. A folder or zip mod needs its <c>modinfo.json</c>: the game
    /// reads that file, not the dll's own attribute, for those two shapes.</summary>
    private string MakeMod(string shape, string dll)
    {
        const string modInfo =
            """{ "type": "code", "modid": "bindingfixture", "name": "Atlas Binding Fixture", "version": "0.1.0", "side": "Server" }""";
        string root = Directory.CreateDirectory(Path.Combine(_work.FullName, shape)).FullName;

        if (shape == "dll")
        {
            string copy = Path.Combine(root, ModDll);
            File.Copy(dll, copy);
            return copy;
        }

        string folder = Path.Combine(root, "bindingfixture");
        Directory.CreateDirectory(folder);
        File.Copy(dll, Path.Combine(folder, ModDll));
        File.WriteAllText(Path.Combine(folder, "modinfo.json"), modInfo);
        if (shape == "folder")
        {
            return folder;
        }

        string zip = Path.Combine(root, "bindingfixture.zip");
        ZipFile.CreateFromDirectory(folder, zip);
        return zip;
    }
}
