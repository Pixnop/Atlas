using Atlas.Engine.Tests.Support;

namespace Atlas.Engine.Tests;

/// <summary>Covers what the staged-mod check says about a folder mod that ships a library next to
/// its own dll, against DependentFixtureMod (a mod that uses DependencyLibraryFixture at
/// start-up). The mod's dll is not next to this assembly, so the engine binds the first copy a
/// host stages, which is what makes the "bound by an earlier boot" note reachable: every boot
/// after the first in the process binds that copy, not its own.</summary>
[Trait("Category", "E2E")]
public sealed class StagedModDependencyTests : IDisposable
{
    private const string ModDll = "DependentFixtureMod.dll";

    private static readonly string BuiltMod = Path.Combine(TestPaths.OwnOutputDirectory, "dependent-mod", ModDll);

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("atlas-dependent-");

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

    private static string[] StagedModLines(string stderr)
        => [.. stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("[Atlas] staged mod ", StringComparison.Ordinal))];

    /// <summary>Lays the fixture mod out as a folder mod: its dll and the <c>modinfo.json</c> the
    /// game reads for a folder.</summary>
    private string MakeFolderMod()
    {
        string folder = Path.Combine(Directory.CreateDirectory(Path.Combine(_work.FullName, "mod")).FullName, "dependentfixture");
        Directory.CreateDirectory(folder);
        File.Copy(BuiltMod, Path.Combine(folder, ModDll));
        File.WriteAllText(
            Path.Combine(folder, "modinfo.json"),
            """{ "type": "code", "modid": "dependentfixture", "name": "Atlas Dependent Fixture", "version": "0.1.0", "side": "Server" }""");
        return folder;
    }
}
