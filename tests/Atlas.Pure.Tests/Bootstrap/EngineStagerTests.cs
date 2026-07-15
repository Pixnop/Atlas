using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class EngineStagerTests
{
    [Fact]
    public void Evaluate_Should_RewriteDllAndPdb_When_CopiesDivergeAndNothingIsBound()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.True(outcome.Staged);
            Assert.Null(outcome.FailureMessage);
            Assert.Equal("install-dll-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.dll")));
            Assert.Equal("install-pdb-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.pdb")));

            // The atomic-rename temp files must not linger.
            Assert.Empty(Directory.GetFiles(consumer, "*.atlas-staging"));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_LeaveEverythingAlone_When_CopiesAreIdentical()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            // Use a real assembly so the identity read exercises the assembly-version path too.
            string source = typeof(EngineStagerTests).Assembly.Location;
            File.Copy(source, Path.Combine(consumer, "VintagestoryAPI.dll"));
            File.Copy(source, Path.Combine(install, "VintagestoryAPI.dll"));
            File.WriteAllText(Path.Combine(consumer, "VintagestoryAPI.pdb"), "consumer-pdb");
            File.WriteAllText(Path.Combine(install, "VintagestoryAPI.pdb"), "install-pdb");

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.Null(outcome.FailureMessage);

            // Identical dlls must not trigger the pdb rewrite either: the pair moves as a unit.
            Assert.Equal("consumer-pdb", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.pdb")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_DoNothing_When_ConsumerShipsNoCopy()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.Null(outcome.FailureMessage);
            Assert.False(File.Exists(Path.Combine(consumer, "VintagestoryAPI.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_DoNothing_When_InstallShipsNoApiDll()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "local-dll-bytes", "local-pdb-bytes");

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.Null(outcome.FailureMessage);
            Assert.Equal("local-dll-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_FailButRestage_When_StaleCopyWasAlreadyBound()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");
            string loadedPath = Path.Combine(consumer, "VintagestoryAPI.dll");
            var loaded = new EngineStager.LoadedAssembly(
                loadedPath, EngineStager.TryReadIdentity(loadedPath)!);

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loaded, loadedNewtonsoft: null);

            // This run is doomed (the stale image is bound), but the disk copy was still
            // rewritten so a plain re-run passes without a rebuild; the message says both.
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains(loadedPath, outcome.FailureMessage);
            Assert.Contains("already loaded", outcome.FailureMessage);
            Assert.Contains("re-run the tests", outcome.FailureMessage);
            Assert.True(outcome.Staged);
            Assert.Equal("install-dll-bytes", File.ReadAllText(loadedPath));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_Fail_When_InstallShipsDllWithoutPdb()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "install-dll-bytes");

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains("VintagestoryAPI.pdb", outcome.FailureMessage);
            Assert.Equal("stale-dll-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_Fail_When_ConsumerDirectoryIsUnwritable()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");
            File.SetUnixFileMode(
                consumer, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains("writable", outcome.FailureMessage);
        }
        finally
        {
            File.SetUnixFileMode(
                consumer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_ReportUnexpectedIoAsFailure_InsteadOfThrowing()
    {
        // A consumer "directory" that is actually a file: File.Exists on the dll path throws
        // nowhere, but the identity read of the install copy against a bogus consumer path must
        // never tear down a module initializer. Simplest total-function probe: an unreadable
        // local dll.
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");
            string localDll = Path.Combine(consumer, "VintagestoryAPI.dll");
            File.SetUnixFileMode(localDll, UnixFileMode.None);

            EngineStager.Outcome outcome = EngineStager.Evaluate(consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains("staging preflight failed unexpectedly", outcome.FailureMessage);
            Assert.Contains(localDll, outcome.FailureMessage);
        }
        finally
        {
            File.SetUnixFileMode(
                Path.Combine(consumer, "VintagestoryAPI.dll"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryStageEarly_Should_Stage_When_InstallIsValid()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            WritePair(install, "install-dll-bytes", "install-pdb-bytes");
            File.WriteAllText(Path.Combine(install, "VintagestoryLib.dll"), "lib-stub");

            EngineStager.TryStageEarly(consumer, install);

            // Whatever the process's real loaded-assembly state (another test may have bound
            // the genuine VintagestoryAPI), the disk copy must equal the install's afterwards:
            // Stage rewrites it, and FailLoadedStale re-stages it for the next run.
            Assert.Equal("install-dll-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.dll")));
            Assert.Equal("install-pdb-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.pdb")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryStageEarly_Should_DoNothing_When_InstallIsUnsetOrInvalid()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");

            // No VintagestoryLib.dll: not a usable install; and a null install must no-op too.
            EngineStager.TryStageEarly(consumer, install);
            EngineStager.TryStageEarly(consumer, installDir: null);

            Assert.Equal("stale-dll-bytes", File.ReadAllText(Path.Combine(consumer, "VintagestoryAPI.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryStageEarly_Should_SwallowEvenBogusPaths()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            // Module-initializer contract: never throw, whatever the inputs. A consumer path
            // with an embedded NUL makes Path.GetFullPath throw inside the evaluation, which the
            // trigger must reduce to a stderr line; the install must be valid to get that far.
            File.WriteAllText(Path.Combine(install, "VintagestoryLib.dll"), "lib-stub");

            EngineStager.TryStageEarly("\0invalid\0", install);
            EngineStager.TryStageEarly(consumer, "\0also-invalid\0");
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void EnsureStagedForBoot_Should_Pass_When_CopiesAreIdentical()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            string source = typeof(EngineStagerTests).Assembly.Location;
            File.Copy(source, Path.Combine(consumer, "VintagestoryAPI.dll"));
            File.Copy(source, Path.Combine(install, "VintagestoryAPI.dll"));

            EngineStager.EnsureStagedForBoot(consumer, install);
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void EnsureStagedForBoot_Should_ThrowSetupException_When_StagingIsImpossible()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            // Divergent copies and an install without its pdb: whichever branch the process's
            // real loaded-assembly state selects (FailLoadedStale when another test already
            // bound the genuine VintagestoryAPI, FailInstallPdbMissing otherwise), the boot
            // preflight must surface an actionable setup error.
            WritePair(consumer, "stale-dll-bytes", "stale-pdb-bytes");
            File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "install-dll-bytes");

            var ex = Assert.Throws<AtlasSetupException>(
                () => EngineStager.EnsureStagedForBoot(consumer, install));

            Assert.Contains("VintagestoryAPI", ex.Message);
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryReadIdentity_Should_ReturnNull_When_FileMissing()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            Assert.Null(EngineStager.TryReadIdentity(Path.Combine(consumer, "VintagestoryAPI.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryReadIdentity_Should_ReadVersion_ForARealAssembly_AndNullForPlainBytes()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            string assemblyPath = Path.Combine(consumer, "real.dll");
            File.Copy(typeof(EngineStagerTests).Assembly.Location, assemblyPath);
            string bytesPath = Path.Combine(consumer, "plain.dll");
            File.WriteAllText(bytesPath, "not-an-assembly");

            ApiCopySync.FileIdentity? real = EngineStager.TryReadIdentity(assemblyPath);
            ApiCopySync.FileIdentity? plain = EngineStager.TryReadIdentity(bytesPath);

            Assert.NotNull(real);
            Assert.NotNull(real!.AssemblyVersion);
            Assert.NotNull(plain);
            Assert.Null(plain!.AssemblyVersion);
            Assert.Equal(15, plain.Length);
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_StageOlderNewtonsoft_When_NothingIsBound()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            // Real PE images with orderable file versions: this test assembly (0.1.0.0) plays
            // the older build-time copy, xunit.assert (2.x) the newer install copy.
            string older = typeof(EngineStagerTests).Assembly.Location;
            string newer = typeof(Xunit.Assert).Assembly.Location;
            Directory.CreateDirectory(Path.Combine(install, "Lib"));
            File.Copy(older, Path.Combine(consumer, "Newtonsoft.Json.dll"));
            File.Copy(newer, Path.Combine(install, "Lib", "Newtonsoft.Json.dll"));

            EngineStager.Outcome outcome = EngineStager.Evaluate(
                consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.True(outcome.Staged);
            Assert.Null(outcome.FailureMessage);
            Assert.Equal(
                File.ReadAllBytes(newer),
                File.ReadAllBytes(Path.Combine(consumer, "Newtonsoft.Json.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_LeaveNewerNewtonsoftAlone()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            // The forward direction: the output carries the NEWER game build (superset,
            // measured green); staging must never downgrade it.
            string older = typeof(EngineStagerTests).Assembly.Location;
            string newer = typeof(Xunit.Assert).Assembly.Location;
            Directory.CreateDirectory(Path.Combine(install, "Lib"));
            File.Copy(newer, Path.Combine(consumer, "Newtonsoft.Json.dll"));
            File.Copy(older, Path.Combine(install, "Lib", "Newtonsoft.Json.dll"));

            EngineStager.Outcome outcome = EngineStager.Evaluate(
                consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.Null(outcome.FailureMessage);
            Assert.Equal(
                File.ReadAllBytes(newer),
                File.ReadAllBytes(Path.Combine(consumer, "Newtonsoft.Json.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_FailButRestage_When_OlderNewtonsoftWasAlreadyBound()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            string older = typeof(EngineStagerTests).Assembly.Location;
            string newer = typeof(Xunit.Assert).Assembly.Location;
            Directory.CreateDirectory(Path.Combine(install, "Lib"));
            string localPath = Path.Combine(consumer, "Newtonsoft.Json.dll");
            File.Copy(older, localPath);
            File.Copy(newer, Path.Combine(install, "Lib", "Newtonsoft.Json.dll"));
            var loaded = new EngineStager.LoadedAssembly(
                localPath, EngineStager.TryReadIdentity(localPath)!);
            Version loadedVersionBeforeRestage = EngineStager.TryReadFileVersion(localPath)!;

            EngineStager.Outcome outcome = EngineStager.Evaluate(
                consumer, install, loadedApi: null, loadedNewtonsoft: loaded);

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
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void TryReadFileVersion_Should_ReadPeImages_AndNullEverythingElse()
    {
        (string consumer, string install) = CreateDirs();
        try
        {
            string text = Path.Combine(consumer, "plain.dll");
            File.WriteAllText(text, "not-a-pe-image");

            Assert.NotNull(EngineStager.TryReadFileVersion(typeof(EngineStagerTests).Assembly.Location));
            Assert.Null(EngineStager.TryReadFileVersion(text));
            Assert.Null(EngineStager.TryReadFileVersion(Path.Combine(consumer, "missing.dll")));
        }
        finally
        {
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_Fail_When_OlderNewtonsoftAndConsumerIsUnwritable()
    {
        // The API decision resolves to None (no local copy shadows probing), so the Newtonsoft
        // decision runs: an OLDER game build in a read-only output is the reverse direction the
        // boot must refuse AND cannot rewrite, so it degrades with an actionable "writable"
        // message rather than crashing. Mirror of the API-side unwritable test.
        (string consumer, string install) = CreateDirs();
        try
        {
            string older = typeof(EngineStagerTests).Assembly.Location;
            string newer = typeof(Xunit.Assert).Assembly.Location;
            Directory.CreateDirectory(Path.Combine(install, "Lib"));
            File.Copy(older, Path.Combine(consumer, "Newtonsoft.Json.dll"));
            File.Copy(newer, Path.Combine(install, "Lib", "Newtonsoft.Json.dll"));
            File.SetUnixFileMode(consumer, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            EngineStager.Outcome outcome = EngineStager.Evaluate(
                consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains("Newtonsoft.Json.dll", outcome.FailureMessage);
            Assert.Contains("writable", outcome.FailureMessage);
        }
        finally
        {
            File.SetUnixFileMode(
                consumer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            DeleteDirs(consumer, install);
        }
    }

    [Fact]
    public void Evaluate_Should_ReportUnexpectedNewtonsoftIoAsFailure_InsteadOfThrowing()
    {
        // Total-function guarantee for the module-initializer callers on the Newtonsoft path too:
        // an unreadable local Newtonsoft copy makes the identity read throw, which must surface at
        // boot as a setup error naming the cause, never tear down a type initializer.
        (string consumer, string install) = CreateDirs();
        string localNewtonsoft = Path.Combine(consumer, "Newtonsoft.Json.dll");
        try
        {
            Directory.CreateDirectory(Path.Combine(install, "Lib"));
            File.Copy(typeof(EngineStagerTests).Assembly.Location, localNewtonsoft);
            File.Copy(typeof(Xunit.Assert).Assembly.Location, Path.Combine(install, "Lib", "Newtonsoft.Json.dll"));
            File.SetUnixFileMode(localNewtonsoft, UnixFileMode.None);

            EngineStager.Outcome outcome = EngineStager.Evaluate(
                consumer, install, loadedApi: null, loadedNewtonsoft: null);

            Assert.False(outcome.Staged);
            Assert.NotNull(outcome.FailureMessage);
            Assert.Contains("staging preflight failed unexpectedly", outcome.FailureMessage);
            Assert.Contains(localNewtonsoft, outcome.FailureMessage);
        }
        finally
        {
            File.SetUnixFileMode(localNewtonsoft, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            DeleteDirs(consumer, install);
        }
    }

    private static (string Consumer, string Install) CreateDirs() =>
        (Directory.CreateTempSubdirectory("atlas-staging-consumer").FullName,
         Directory.CreateTempSubdirectory("atlas-staging-install").FullName);

    private static void WritePair(string dir, string dllContent, string pdbContent)
    {
        File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.dll"), dllContent);
        File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.pdb"), pdbContent);
    }

    private static void DeleteDirs(params string[] dirs)
    {
        foreach (string dir in dirs)
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
