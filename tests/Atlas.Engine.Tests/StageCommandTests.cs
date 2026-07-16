using Atlas.Cli;

namespace Atlas.Engine.Tests;

/// <summary>Drives `atlas stage` (<see cref="StageRunner"/>) against real, copied test-output
/// directories, the same way <see cref="DiffCommandTests"/> drives the diff command: no server
/// boot, so plain xunit facts suffice. The fixture files (Atlas.dll, VintagestoryAPI.dll+pdb,
/// Newtonsoft.Json.dll) come from THIS project's own build output rather than
/// AppContext.BaseDirectory: other tests in this shared process boot real servers, which
/// redirects AppContext.BaseDirectory to the install (see GameEnvironment.Initialize), so the
/// stable reference is this test assembly's own Location.</summary>
[Trait("Category", "E2E")]
public class StageCommandTests : IDisposable
{
    private static readonly string OwnOutputDirectory =
        Path.GetDirectoryName(typeof(StageCommandTests).Assembly.Location)!;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-stagecmd-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Stage_Should_FlipTheApiDllBytes_When_TargetWasBuiltAgainstADifferentInstall()
    {
        string? altInstall = AlternateInstallDirectory();
        if (altInstall is null)
        {
            // No second install provisioned in this environment (CI provisions exactly one, via
            // VINTAGE_STORY); see AlternateInstallDirectory for the local convention this checks.
            return;
        }

        string target = CopyOwnOutputAsTestOutput("cross-install");
        byte[] beforeApiDll = File.ReadAllBytes(Path.Combine(target, "VintagestoryAPI.dll"));
        byte[] installApiDll = File.ReadAllBytes(Path.Combine(altInstall, "VintagestoryAPI.dll"));
        Assert.NotEqual(installApiDll, beforeApiDll); // sanity: the fixture must actually diverge

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = StageRunner.Run(new StageArguments(target), altInstall, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("VintagestoryAPI.dll and VintagestoryAPI.pdb: staged", output.ToString());
        Assert.Equal(installApiDll, File.ReadAllBytes(Path.Combine(target, "VintagestoryAPI.dll")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(altInstall, "VintagestoryAPI.pdb")),
            File.ReadAllBytes(Path.Combine(target, "VintagestoryAPI.pdb")));
    }

    [Fact]
    public void Stage_Should_AcceptADllPath_And_ResolveItsDirectory()
    {
        string? altInstall = AlternateInstallDirectory();
        if (altInstall is null)
        {
            return;
        }

        string target = CopyOwnOutputAsTestOutput("dll-path");
        string dllPath = Path.Combine(target, "Atlas.GuineaPig.Scenarios.dll");
        File.WriteAllText(dllPath, "stand-in for a scenario assembly; only the directory matters");
        var output = new StringWriter();

        int exitCode = StageRunner.Run(new StageArguments(dllPath), altInstall, output, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("staged", output.ToString());
    }

    [Fact]
    public void Stage_Should_ReportNoChangesAndExitZero_When_TargetAlreadyMatchesTheInstall()
    {
        string install = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string target = CopyOwnOutputAsTestOutput("noop");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = StageRunner.Run(new StageArguments(target), install, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string text = output.ToString();
        Assert.Contains("VintagestoryAPI.dll and VintagestoryAPI.pdb: already matches", text);
        Assert.Contains("Newtonsoft.Json.dll: already matches", text);
    }

    [Fact]
    public void Stage_Should_ReportNothingToStage_And_ExitZero_When_TargetHasNoLocalApiCopy()
    {
        string install = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string target = _root.CreateSubdirectory("bare").FullName;
        File.Copy(
            Path.Combine(OwnOutputDirectory, "Atlas.dll"), Path.Combine(target, "Atlas.dll"));
        var output = new StringWriter();

        int exitCode = StageRunner.Run(new StageArguments(target), install, output, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("VintagestoryAPI.dll and VintagestoryAPI.pdb: no local copy to stage", output.ToString());
    }

    [Fact]
    public void Stage_Should_ExitTwo_When_TheTargetDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_root.FullName, "does-not-exist");
        var error = new StringWriter();

        int exitCode = StageRunner.Run(
            new StageArguments(missing),
            Environment.GetEnvironmentVariable("VINTAGE_STORY")!,
            new StringWriter(),
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains("stage target directory not found", error.ToString());
    }

    [Fact]
    public void Stage_Should_ExitTwo_And_ReportTheMissingInstallPdb_When_InstallShipsNoPdb()
    {
        // Cheap failure-path fixture, mirroring EngineStagerTests' own style: plain diverged
        // bytes (no real PE image needed) for the local/install dll pair, an install missing its
        // pdb. Only Atlas.dll itself must be real, so StageAssemblyResolver can load it.
        string target = _root.CreateSubdirectory("no-install-pdb-target").FullName;
        string install = _root.CreateSubdirectory("no-install-pdb-install").FullName;
        File.Copy(Path.Combine(OwnOutputDirectory, "Atlas.dll"), Path.Combine(target, "Atlas.dll"));
        File.WriteAllText(Path.Combine(target, "VintagestoryAPI.dll"), "stale-dll-bytes");
        File.WriteAllText(Path.Combine(target, "VintagestoryAPI.pdb"), "stale-pdb-bytes");
        File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "install-dll-bytes-no-pdb");
        var error = new StringWriter();

        int exitCode = StageRunner.Run(new StageArguments(target), install, new StringWriter(), error);

        Assert.Equal(2, exitCode);
        string text = error.ToString();
        Assert.Contains("VintagestoryAPI.pdb", text);
        Assert.Contains("cannot auto-stage", text);

        // The core never rewrites the pair when it cannot bring the pdb along.
        Assert.Equal("stale-dll-bytes", File.ReadAllText(Path.Combine(target, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void Stage_Should_SucceedWithoutLoadingTheEngineAssembly_When_CopiesAreNotEvenValidPeImages()
    {
        // Proves the engine-free path contract: EngineStaging/EngineStager read file bytes and PE
        // metadata (AssemblyName.GetAssemblyName, which reads metadata without loading the image
        // into the CLR); they never Assembly.Load a VintagestoryAPI.dll copy. A real load attempt
        // on these garbage bytes would throw BadImageFormatException; staging them cleanly instead
        // is the observable proof that no such attempt happens.
        string target = _root.CreateSubdirectory("garbage-target").FullName;
        string install = _root.CreateSubdirectory("garbage-install").FullName;
        File.Copy(Path.Combine(OwnOutputDirectory, "Atlas.dll"), Path.Combine(target, "Atlas.dll"));
        File.WriteAllText(Path.Combine(target, "VintagestoryAPI.dll"), "not-a-real-assembly-local");
        File.WriteAllText(Path.Combine(target, "VintagestoryAPI.pdb"), "not-a-real-pdb-local");
        File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "not-a-real-assembly-install");
        File.WriteAllText(Path.Combine(install, "VintagestoryAPI.pdb"), "not-a-real-pdb-install");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = StageRunner.Run(new StageArguments(target), install, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("staged", output.ToString());
        Assert.Equal("not-a-real-assembly-install", File.ReadAllText(Path.Combine(target, "VintagestoryAPI.dll")));
    }

    /// <summary>Copies this project's own build output's engine-provided files into a fresh temp
    /// directory, standing in for "a compiled test output" without needing a second project.</summary>
    /// <param name="name">A short label for the temp subdirectory.</param>
    /// <returns>The copied directory's full path.</returns>
    private string CopyOwnOutputAsTestOutput(string name)
    {
        string target = _root.CreateSubdirectory(name).FullName;
        foreach (string fileName in new[]
                 {
                     "Atlas.dll", "VintagestoryAPI.dll", "VintagestoryAPI.pdb", "Newtonsoft.Json.dll",
                 })
        {
            string source = Path.Combine(OwnOutputDirectory, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(target, fileName));
            }
        }

        return target;
    }

    /// <summary>Locates a second, different-version Vintage Story install for the cross-install
    /// proof: ATLAS_TEST_ALT_INSTALL if set, otherwise the ~/dev/.vs-compat/1.21.7 convention
    /// docs/specs/2026-07-12-pre-122-compat.md documents. Neither is provisioned in CI (which
    /// downloads exactly one install), so callers treat a null return as "skip this assertion",
    /// not a failure.</summary>
    /// <returns>The install directory, or null when none is available here.</returns>
    private static string? AlternateInstallDirectory()
    {
        string candidate = Environment.GetEnvironmentVariable("ATLAS_TEST_ALT_INSTALL")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev", ".vs-compat", "1.21.7");
        return File.Exists(Path.Combine(candidate, "VintagestoryLib.dll")) ? candidate : null;
    }
}
