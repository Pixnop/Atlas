using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class EngineStagingTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string HashC = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    private static readonly ApiCopySync.FileIdentity Local = new(1024, HashA, "1.22.3.0");
    private static readonly ApiCopySync.FileIdentity Install = new(2048, HashB, "1.0.0.0");

    [Fact]
    public void Decide_Should_ReturnNone_When_NoLocalCopyShadowsProbing()
    {
        // Private=false consumers: probing falls through to the install's own copy.
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.Decide(local: null, Install, installPdbPresent: true, loaded: null));
    }

    [Fact]
    public void Decide_Should_ReturnNone_When_InstallShipsNoApiDll()
    {
        // Locate() vetted the install already; a missing install-side dll leaves nothing to
        // stage from, and a later boot failure must point at the broken install instead.
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.Decide(Local, install: null, installPdbPresent: true, loaded: null));
    }

    [Fact]
    public void Decide_Should_ReturnNone_When_CopiesAreIdentical()
    {
        // The documented rebuild flow: output built against this exact install.
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.Decide(Local, Local, installPdbPresent: true, loaded: null));
    }

    [Fact]
    public void Decide_Should_Stage_When_DivergentAndNotLoadedYet()
    {
        // The issue #49 core case: VINTAGE_STORY repointed without a rebuild, caught by a
        // module-initializer trigger before anything bound the copy.
        Assert.Equal(
            EngineStaging.StageAction.Stage,
            EngineStaging.Decide(Local, Install, installPdbPresent: true, loaded: null));
    }

    [Fact]
    public void Decide_Should_Stage_When_LoadedCopyAlreadyMatchesInstall()
    {
        // The process is already bound to the install's bytes (loaded from the install itself);
        // only the disk copy lags, and rewriting it keeps the next run coherent too.
        Assert.Equal(
            EngineStaging.StageAction.Stage,
            EngineStaging.Decide(Local, Install, installPdbPresent: true, loaded: Install));
    }

    [Fact]
    public void Decide_Should_FailLoadedStale_When_DivergedCopyIsAlreadyBound()
    {
        // Engine types were JITted before any trigger ran: the loaded image cannot be swapped.
        Assert.Equal(
            EngineStaging.StageAction.FailLoadedStale,
            EngineStaging.Decide(Local, Install, installPdbPresent: true, loaded: Local));
    }

    [Fact]
    public void Decide_Should_FailLoadedStale_When_LoadedFromSomewhereElseEntirely()
    {
        // A third set of bytes (neither the local copy nor the install): still unswappable.
        var elsewhere = new ApiCopySync.FileIdentity(4096, HashC, null);
        Assert.Equal(
            EngineStaging.StageAction.FailLoadedStale,
            EngineStaging.Decide(Local, Install, installPdbPresent: true, loaded: elsewhere));
    }

    [Fact]
    public void Decide_Should_FailInstallPdbMissing_When_InstallShipsDllWithoutPdb()
    {
        // Staging the dll alone would plant the opaque LoggerBase boot kill the pdb preflight
        // exists to prevent; the pair stages together or not at all.
        Assert.Equal(
            EngineStaging.StageAction.FailInstallPdbMissing,
            EngineStaging.Decide(Local, Install, installPdbPresent: false, loaded: null));
    }

    [Fact]
    public void Decide_Should_PreferLoadedStale_Over_InstallPdbMissing()
    {
        // Both problems at once: the already-bound stale copy is the one that dooms THIS run,
        // so it owns the message.
        Assert.Equal(
            EngineStaging.StageAction.FailLoadedStale,
            EngineStaging.Decide(Local, Install, installPdbPresent: false, loaded: Local));
    }

    [Fact]
    public void DecideNewtonsoft_Should_ReturnNone_When_MissingOrIdentical()
    {
        var v3 = new Version(13, 0, 3);
        var v4 = new Version(13, 0, 4);

        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(null, Install, v3, v4, loaded: null));
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(Local, null, v3, v4, loaded: null));
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(Local, Local, v3, v4, loaded: null));
    }

    [Fact]
    public void DecideNewtonsoft_Should_FailOpen_When_VersionsAreUnorderable()
    {
        // No file version on either side: the forward mix is the measured-green common case,
        // so an unorderable pair must never block a run.
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(Local, Install, null, new Version(13, 0, 4), loaded: null));
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(Local, Install, new Version(13, 0, 3), null, loaded: null));
    }

    [Fact]
    public void DecideNewtonsoft_Should_ReturnNone_When_OutputCarriesSameOrNewerBuild()
    {
        // The forward direction (built against the newer install, run on the older): the
        // output's newer game build is a superset for the older engine, measured green across
        // the whole cross-install matrix. Equal versions with different bytes (a fork's own
        // rebuild) fail open the same way.
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(
                Local, Install, new Version(13, 0, 4, 30916), new Version(13, 0, 3, 27908), loaded: null));
        Assert.Equal(
            EngineStaging.StageAction.None,
            EngineStaging.DecideNewtonsoft(
                Local, Install, new Version(13, 0, 3), new Version(13, 0, 3), loaded: null));
    }

    [Fact]
    public void DecideNewtonsoft_Should_Stage_When_OutputCarriesOlderBuildNotBound()
    {
        // The reverse direction before anything bound it (the `atlas run` flow): rewrite the
        // file and the process binds the install's build.
        Assert.Equal(
            EngineStaging.StageAction.Stage,
            EngineStaging.DecideNewtonsoft(
                Local, Install, new Version(13, 0, 3, 27908), new Version(13, 0, 4, 30916), loaded: null));
    }

    [Fact]
    public void DecideNewtonsoft_Should_Stage_When_BoundCopyAlreadyMatchesInstall()
    {
        Assert.Equal(
            EngineStaging.StageAction.Stage,
            EngineStaging.DecideNewtonsoft(
                Local, Install, new Version(13, 0, 3), new Version(13, 0, 4), loaded: Install));
    }

    [Fact]
    public void DecideNewtonsoft_Should_FailLoadedStale_When_OlderBuildIsAlreadyBound()
    {
        // The VSTest flow: the test host binds the output's copy at process start.
        Assert.Equal(
            EngineStaging.StageAction.FailLoadedStale,
            EngineStaging.DecideNewtonsoft(
                Local, Install, new Version(13, 0, 3), new Version(13, 0, 4), loaded: Local));
    }

    [Fact]
    public void DescribeNewtonsoftStaged_Should_NameBothFilesAndVersions()
    {
        string message = EngineStaging.DescribeNewtonsoftStaged(
            "/tests/bin/Newtonsoft.Json.dll",
            new Version(13, 0, 3, 27908),
            "/install/Lib/Newtonsoft.Json.dll",
            new Version(13, 0, 4, 30916));

        Assert.Contains("/tests/bin/Newtonsoft.Json.dll", message);
        Assert.Contains("/install/Lib/Newtonsoft.Json.dll", message);
        Assert.Contains("13.0.3.27908", message);
        Assert.Contains("13.0.4.30916", message);
    }

    [Fact]
    public void DescribeNewtonsoftLoadedStale_Should_OfferTheReRun_When_Restaged()
    {
        string message = EngineStaging.DescribeNewtonsoftLoadedStale(
            "/tests/bin/Newtonsoft.Json.dll",
            new Version(13, 0, 3),
            "/install/Lib/Newtonsoft.Json.dll",
            new Version(13, 0, 4),
            restaged: true);

        Assert.Contains("already loaded", message);
        Assert.Contains("MissingMethodException", message);
        Assert.Contains("re-run the tests", message);
        Assert.Contains("13.0.3", message);
        Assert.Contains("13.0.4", message);
    }

    [Fact]
    public void DescribeNewtonsoftLoadedStale_Should_HandleUnknownLoadedVersion_AndOfferTheCopy()
    {
        string message = EngineStaging.DescribeNewtonsoftLoadedStale(
            "<in-memory image>",
            null,
            "/install/Lib/Newtonsoft.Json.dll",
            new Version(13, 0, 4),
            restaged: false);

        Assert.Contains("unknown", message);
        Assert.Contains("copy the install's", message);
        Assert.Contains("Lib/Newtonsoft.Json.dll", message);
        Assert.DoesNotContain("re-staged", message);
    }

    [Fact]
    public void DescribeNewtonsoftUnwritable_Should_NameTheIoErrorAndEveryRemedy()
    {
        string message = EngineStaging.DescribeNewtonsoftUnwritable(
            "/tests/bin/Newtonsoft.Json.dll",
            new Version(13, 0, 3),
            "/install/Lib/Newtonsoft.Json.dll",
            new Version(13, 0, 4),
            "Access to the path is denied.");

        Assert.Contains("Access to the path is denied.", message);
        Assert.Contains("writable", message);
        Assert.Contains("rebuild the test project", message);
    }

    [Fact]
    public void DescribeStaged_Should_NameBothFilesAndBothIdentities()
    {
        string message = EngineStaging.DescribeStaged(
            "/tests/bin/VintagestoryAPI.dll", Local, "/install/VintagestoryAPI.dll", Install);

        Assert.Contains("/tests/bin/VintagestoryAPI.dll", message);
        Assert.Contains("/install/VintagestoryAPI.dll", message);
        Assert.Contains("1024 bytes", message);
        Assert.Contains("2048 bytes", message);
        Assert.Contains(HashA[..12], message);
        Assert.Contains(HashB[..12], message);
        Assert.Contains("VintagestoryAPI.pdb", message);
        Assert.Contains("without a rebuild", message);
    }

    [Fact]
    public void DescribeLoadedStale_Should_OfferTheReRun_When_Restaged()
    {
        string message = EngineStaging.DescribeLoadedStale(
            "/tests/bin/VintagestoryAPI.dll", Local, "/install/VintagestoryAPI.dll", Install, restaged: true);

        Assert.Contains("/tests/bin/VintagestoryAPI.dll", message);
        Assert.Contains("/install/VintagestoryAPI.dll", message);
        Assert.Contains(HashA[..12], message);
        Assert.Contains(HashB[..12], message);
        Assert.Contains("already loaded", message);
        Assert.Contains("re-staged", message);
        Assert.Contains("re-run the tests", message);
    }

    [Fact]
    public void DescribeLoadedStale_Should_OfferRebuildAndManualCopy_When_NotRestaged()
    {
        string message = EngineStaging.DescribeLoadedStale(
            "/tests/bin/VintagestoryAPI.dll", Local, "/install/VintagestoryAPI.dll", Install, restaged: false);

        Assert.Contains("Rebuild the test project against this install", message);
        Assert.Contains(
            "copy the install's VintagestoryAPI.dll AND VintagestoryAPI.pdb over the test-output copies",
            message);
        Assert.DoesNotContain("re-staged", message);
    }

    [Fact]
    public void DescribeUnwritable_Should_NameTheIoErrorAndEveryRemedy()
    {
        string message = EngineStaging.DescribeUnwritable(
            "/tests/bin/VintagestoryAPI.dll",
            Local,
            "/install/VintagestoryAPI.dll",
            Install,
            "Access to the path is denied.");

        Assert.Contains("/tests/bin/VintagestoryAPI.dll", message);
        Assert.Contains("/install/VintagestoryAPI.dll", message);
        Assert.Contains("Access to the path is denied.", message);
        Assert.Contains("writable", message);
        Assert.Contains("rebuild the test project against this install", message);
        Assert.Contains("VintagestoryAPI.pdb", message);
    }

    [Fact]
    public void DescribeInstallPdbMissing_Should_NameTheInstallAndTheOpaqueDeath()
    {
        string message = EngineStaging.DescribeInstallPdbMissing(
            "/tests/bin/VintagestoryAPI.dll", "/install/VintagestoryAPI.dll");

        Assert.Contains("/tests/bin/VintagestoryAPI.dll", message);
        Assert.Contains("/install/VintagestoryAPI.dll", message);
        Assert.Contains("VintagestoryAPI.pdb", message);
        Assert.Contains("TypeInitializationException", message);
        Assert.Contains("rebuild the test project", message);
    }
}
