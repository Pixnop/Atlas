using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class EngineStagerTests : IDisposable
{
    private readonly string _consumer = Directory.CreateTempSubdirectory("atlas-staging-consumer").FullName;
    private readonly string _install = Directory.CreateTempSubdirectory("atlas-staging-install").FullName;

    public void Dispose()
    {
        Delete(_consumer);
        Delete(_install);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Stage_Should_RewriteDllAndPdb_When_CopiesDivergeAndNothingIsBound()
    {
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.True(outcome.Staged);
        Assert.Null(outcome.FailureMessage);
        Assert.Equal("install-dll-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.dll")));
        Assert.Equal("install-pdb-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.pdb")));

        // The atomic-rename temp files must not linger.
        Assert.Empty(Directory.GetFiles(_consumer, "*.atlas-staging"));
    }

    [Fact]
    public void Stage_Should_LeaveEverythingAlone_When_CopiesAreIdentical()
    {
        // Use a real assembly so the identity read exercises the assembly-version path too.
        string source = typeof(EngineStagerTests).Assembly.Location;
        File.Copy(source, Path.Combine(_consumer, "VintagestoryAPI.dll"));
        File.Copy(source, Path.Combine(_install, "VintagestoryAPI.dll"));
        File.WriteAllText(Path.Combine(_consumer, "VintagestoryAPI.pdb"), "consumer-pdb");
        File.WriteAllText(Path.Combine(_install, "VintagestoryAPI.pdb"), "install-pdb");

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.Null(outcome.FailureMessage);

        // Identical dlls must not trigger the pdb rewrite either: the pair moves as a unit.
        Assert.Equal("consumer-pdb", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.pdb")));
    }

    [Fact]
    public void Stage_Should_DoNothing_When_ConsumerShipsNoCopy()
    {
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.Null(outcome.FailureMessage);
        Assert.False(File.Exists(Path.Combine(_consumer, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void Stage_Should_DoNothing_When_InstallShipsNoApiDll()
    {
        WritePair(_consumer, "local-dll-bytes", "local-pdb-bytes");

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.Null(outcome.FailureMessage);
        Assert.Equal("local-dll-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void Stage_Should_FailButRestage_When_StaleCopyWasAlreadyBound()
    {
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");
        string loadedPath = Path.Combine(_consumer, "VintagestoryAPI.dll");
        var loaded = new EngineStager.LoadedAssembly(
            loadedPath, EngineStager.TryReadIdentity(loadedPath)!);

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loaded, loadedNewtonsoft: null);

        // This run is doomed (the stale image is bound), but the disk copy was still
        // rewritten so a plain re-run passes without a rebuild; the message says both.
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains(loadedPath, outcome.FailureMessage);
        Assert.Contains("already loaded", outcome.FailureMessage);
        Assert.Contains("re-run the tests", outcome.FailureMessage);
        Assert.True(outcome.Staged);
        Assert.Equal("install-dll-bytes", File.ReadAllText(loadedPath));
    }

    [Fact]
    public void Stage_Should_Fail_When_InstallShipsDllWithoutPdb()
    {
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        File.WriteAllText(Path.Combine(_install, "VintagestoryAPI.dll"), "install-dll-bytes");

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("VintagestoryAPI.pdb", outcome.FailureMessage);
        Assert.Equal("stale-dll-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void Stage_Should_Fail_When_ConsumerDirectoryIsUnwritable()
    {
        // Unix only: the arrange strips a permission bit Windows has no equivalent for. See
        // the note on Delete below; the staging code under test is platform-neutral. On Windows
        // the test returns and passes having asserted nothing, so a Windows green is not
        // coverage of this path; CI is Linux.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");
        File.SetUnixFileMode(_consumer, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("writable", outcome.FailureMessage);
    }

    [Fact]
    public void Stage_Should_ReportUnexpectedIoAsFailure_InsteadOfThrowing()
    {
        // A consumer "directory" that is actually a file: File.Exists on the dll path throws
        // nowhere, but the identity read of the install copy against a bogus consumer path must
        // never tear down a module initializer. Simplest total-function probe: an unreadable
        // local dll.
        //
        // Unix only: an unreadable file is arranged through its permission bits, which Windows
        // has no equivalent for. The staging code under test is platform-neutral. On Windows the
        // test returns and passes having asserted nothing, so a Windows green is not coverage of
        // this path; CI is Linux.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");
        string localDll = Path.Combine(_consumer, "VintagestoryAPI.dll");
        File.SetUnixFileMode(localDll, UnixFileMode.None);

        EngineStager.Outcome outcome = EngineStager.Stage(_consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("staging preflight failed unexpectedly", outcome.FailureMessage);
        Assert.Contains(localDll, outcome.FailureMessage);
    }

    [Fact]
    public void TryStageEarly_Should_Stage_When_InstallIsValid()
    {
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        WritePair(_install, "install-dll-bytes", "install-pdb-bytes");
        File.WriteAllText(Path.Combine(_install, "VintagestoryLib.dll"), "lib-stub");

        EngineStager.TryStageEarly(_consumer, _install);

        // Whatever the process's real loaded-assembly state (another test may have bound
        // the genuine VintagestoryAPI), the disk copy must equal the install's afterwards:
        // Stage rewrites it, and FailLoadedStale re-stages it for the next run.
        Assert.Equal("install-dll-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.dll")));
        Assert.Equal("install-pdb-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.pdb")));
    }

    [Fact]
    public void TryStageEarly_Should_DoNothing_When_InstallIsUnsetOrInvalid()
    {
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");

        // No VintagestoryLib.dll: not a usable install; and a null install must no-op too.
        EngineStager.TryStageEarly(_consumer, _install);
        EngineStager.TryStageEarly(_consumer, installDir: null);

        Assert.Equal("stale-dll-bytes", File.ReadAllText(Path.Combine(_consumer, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void TryStageEarly_Should_SwallowEvenBogusPaths()
    {
        // Module-initializer contract: never throw, whatever the inputs. A consumer path
        // with an embedded NUL makes Path.GetFullPath throw inside the evaluation, which the
        // trigger must reduce to a stderr line; the install must be valid to get that far.
        File.WriteAllText(Path.Combine(_install, "VintagestoryLib.dll"), "lib-stub");

        Assert.Null(Record.Exception(() => EngineStager.TryStageEarly("\0invalid\0", _install)));
        Assert.Null(Record.Exception(() => EngineStager.TryStageEarly(_consumer, "\0also-invalid\0")));
    }

    [Fact]
    public void EnsureStagedForBoot_Should_Pass_When_CopiesAreIdentical()
    {
        string source = typeof(EngineStagerTests).Assembly.Location;
        File.Copy(source, Path.Combine(_consumer, "VintagestoryAPI.dll"));
        File.Copy(source, Path.Combine(_install, "VintagestoryAPI.dll"));

        Assert.Null(Record.Exception(() => EngineStager.EnsureStagedForBoot(_consumer, _install)));
    }

    [Fact]
    public void EnsureStagedForBoot_Should_ThrowSetupException_When_StagingIsImpossible()
    {
        // Divergent copies and an install without its pdb: whichever branch the process's
        // real loaded-assembly state selects (FailLoadedStale when another test already
        // bound the genuine VintagestoryAPI, FailInstallPdbMissing otherwise), the boot
        // preflight must surface an actionable setup error.
        WritePair(_consumer, "stale-dll-bytes", "stale-pdb-bytes");
        File.WriteAllText(Path.Combine(_install, "VintagestoryAPI.dll"), "install-dll-bytes");

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => EngineStager.EnsureStagedForBoot(_consumer, _install));

        Assert.Contains("VintagestoryAPI", ex.Message);
    }

    [Fact]
    public void TryReadIdentity_Should_ReturnNull_When_FileMissing()
    {
        Assert.Null(EngineStager.TryReadIdentity(Path.Combine(_consumer, "VintagestoryAPI.dll")));
    }

    [Fact]
    public void TryReadIdentity_Should_ReadVersion_ForARealAssembly_AndNullForPlainBytes()
    {
        string assemblyPath = Path.Combine(_consumer, "real.dll");
        File.Copy(typeof(EngineStagerTests).Assembly.Location, assemblyPath);
        string bytesPath = Path.Combine(_consumer, "plain.dll");
        File.WriteAllText(bytesPath, "not-an-assembly");

        ApiCopySync.FileIdentity? real = EngineStager.TryReadIdentity(assemblyPath);
        ApiCopySync.FileIdentity? plain = EngineStager.TryReadIdentity(bytesPath);

        Assert.NotNull(real);
        Assert.NotNull(real!.AssemblyVersion);
        Assert.NotNull(plain);
        Assert.Null(plain!.AssemblyVersion);
        Assert.Equal(15, plain.Length);
    }

    [Fact]
    public void Stage_Should_StageOlderNewtonsoft_When_NothingIsBound()
    {
        // Real PE images with orderable file versions: this test assembly plays the older
        // build-time copy, xunit.assert (2.x) the newer install copy. The order comes from
        // Atlas's own <Version>, so it holds only while Atlas stays below 2.x; give these
        // two tests their own pinned images before that stops being true.
        string older = typeof(EngineStagerTests).Assembly.Location;
        string newer = typeof(Xunit.Assert).Assembly.Location;
        Directory.CreateDirectory(Path.Combine(_install, "Lib"));
        File.Copy(older, Path.Combine(_consumer, "Newtonsoft.Json.dll"));
        File.Copy(newer, Path.Combine(_install, "Lib", "Newtonsoft.Json.dll"));

        EngineStager.Outcome outcome = EngineStager.Stage(
            _consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.True(outcome.Staged);
        Assert.Null(outcome.FailureMessage);
        Assert.Equal(
            File.ReadAllBytes(newer),
            File.ReadAllBytes(Path.Combine(_consumer, "Newtonsoft.Json.dll")));
    }

    [Fact]
    public void Stage_Should_LeaveNewerNewtonsoftAlone()
    {
        // The forward direction: the output carries the NEWER game build (superset,
        // measured green); staging must never downgrade it.
        string older = typeof(EngineStagerTests).Assembly.Location;
        string newer = typeof(Xunit.Assert).Assembly.Location;
        Directory.CreateDirectory(Path.Combine(_install, "Lib"));
        File.Copy(newer, Path.Combine(_consumer, "Newtonsoft.Json.dll"));
        File.Copy(older, Path.Combine(_install, "Lib", "Newtonsoft.Json.dll"));

        EngineStager.Outcome outcome = EngineStager.Stage(
            _consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.Null(outcome.FailureMessage);
        Assert.Equal(
            File.ReadAllBytes(newer),
            File.ReadAllBytes(Path.Combine(_consumer, "Newtonsoft.Json.dll")));
    }

    [Fact]
    public void Stage_Should_FailButRestage_When_OlderNewtonsoftWasAlreadyBound()
    {
        string older = typeof(EngineStagerTests).Assembly.Location;
        string newer = typeof(Xunit.Assert).Assembly.Location;
        Directory.CreateDirectory(Path.Combine(_install, "Lib"));
        string localPath = Path.Combine(_consumer, "Newtonsoft.Json.dll");
        File.Copy(older, localPath);
        File.Copy(newer, Path.Combine(_install, "Lib", "Newtonsoft.Json.dll"));
        var loaded = new EngineStager.LoadedAssembly(
            localPath, EngineStager.TryReadIdentity(localPath)!);
        Version loadedVersionBeforeRestage = EngineStager.TryReadFileVersion(localPath)!;

        EngineStager.Outcome outcome = EngineStager.Stage(
            _consumer, _install, loadedApi: null, loadedNewtonsoft: loaded);

        // The VSTest host bound the old build at process start: this run is doomed, but
        // the disk copy was rewritten so a plain re-run passes without a rebuild. The
        // message must carry the version that was BOUND, not the freshly restaged bytes'.
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("Newtonsoft.Json.dll", outcome.FailureMessage);
        Assert.Contains("already loaded", outcome.FailureMessage);
        Assert.Contains("re-run the tests", outcome.FailureMessage);
        Assert.Contains(loadedVersionBeforeRestage.ToString(), outcome.FailureMessage);
        Assert.True(outcome.Staged);
        Assert.Equal(File.ReadAllBytes(newer), File.ReadAllBytes(localPath));
    }

    [Fact]
    public void TryReadFileVersion_Should_ReadPeImages_AndNullEverythingElse()
    {
        string text = Path.Combine(_consumer, "plain.dll");
        File.WriteAllText(text, "not-a-pe-image");

        Assert.NotNull(EngineStager.TryReadFileVersion(typeof(EngineStagerTests).Assembly.Location));
        Assert.Null(EngineStager.TryReadFileVersion(text));
        Assert.Null(EngineStager.TryReadFileVersion(Path.Combine(_consumer, "missing.dll")));
    }

    [Fact]
    public void Stage_Should_Fail_When_OlderNewtonsoftAndConsumerIsUnwritable()
    {
        // The API decision resolves to None (no local copy shadows probing), so the Newtonsoft
        // decision runs: an OLDER game build in a read-only output is the reverse direction the
        // boot must refuse AND cannot rewrite, so it degrades with an actionable "writable"
        // message rather than crashing. Mirror of the API-side unwritable test.
        //
        // Unix only: the arrange strips a permission bit Windows has no equivalent for. The
        // staging code under test is platform-neutral. On Windows the test returns and passes
        // having asserted nothing, so a Windows green is not coverage of this path; CI is Linux.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string older = typeof(EngineStagerTests).Assembly.Location;
        string newer = typeof(Xunit.Assert).Assembly.Location;
        Directory.CreateDirectory(Path.Combine(_install, "Lib"));
        File.Copy(older, Path.Combine(_consumer, "Newtonsoft.Json.dll"));
        File.Copy(newer, Path.Combine(_install, "Lib", "Newtonsoft.Json.dll"));
        File.SetUnixFileMode(_consumer, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        EngineStager.Outcome outcome = EngineStager.Stage(
            _consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("Newtonsoft.Json.dll", outcome.FailureMessage);
        Assert.Contains("writable", outcome.FailureMessage);
    }

    [Fact]
    public void Stage_Should_ReportUnexpectedNewtonsoftIoAsFailure_InsteadOfThrowing()
    {
        // Total-function guarantee for the module-initializer callers on the Newtonsoft path too:
        // an unreadable local Newtonsoft copy makes the identity read throw, which must surface at
        // boot as a setup error naming the cause, never tear down a type initializer.
        //
        // Unix only: an unreadable file is arranged through its permission bits, which Windows
        // has no equivalent for. The staging code under test is platform-neutral. On Windows the
        // test returns and passes having asserted nothing, so a Windows green is not coverage of
        // this path; CI is Linux.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string localNewtonsoft = Path.Combine(_consumer, "Newtonsoft.Json.dll");
        Directory.CreateDirectory(Path.Combine(_install, "Lib"));
        File.Copy(typeof(EngineStagerTests).Assembly.Location, localNewtonsoft);
        File.Copy(typeof(Xunit.Assert).Assembly.Location, Path.Combine(_install, "Lib", "Newtonsoft.Json.dll"));
        File.SetUnixFileMode(localNewtonsoft, UnixFileMode.None);

        EngineStager.Outcome outcome = EngineStager.Stage(
            _consumer, _install, loadedApi: null, loadedNewtonsoft: null);

        Assert.False(outcome.Staged);
        Assert.NotNull(outcome.FailureMessage);
        Assert.Contains("staging preflight failed unexpectedly", outcome.FailureMessage);
        Assert.Contains(localNewtonsoft, outcome.FailureMessage);
    }

    private static void WritePair(string dir, string dllContent, string pdbContent)
    {
        File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.dll"), dllContent);
        File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.pdb"), pdbContent);
    }

    /// <summary>Deletes one fixture directory, first restoring the owner permissions the
    /// unwritable-output tests strip: Directory.Delete needs write and execute on the directory
    /// itself, and a test that fails mid-way never gets to put them back.</summary>
    /// <param name="dir">The directory to delete.</param>
    private static void Delete(string dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(dir, recursive: true);
    }
}
