using Atlas.Api;
using Atlas.Internal.Staging;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Staging;

public class StagedModBindingTests
{
    private static readonly Guid StagedMvid = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BoundMvid = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Verify_Should_ReportVerified_When_TheBoundAssemblyIsTheStagedBuild()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", StagedMvid));

        Assert.False(verdict.Mismatch);
        Assert.Equal(
            "[Atlas] staged mod 'mymod': verified (MVID 11111111-1111-1111-1111-111111111111, loaded from '/tests/bin/Mod.dll')",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_NameTheClassWhoseBootItIs_When_AnOwnerIsGiven()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", StagedMvid), "My.Scenarios.PlayerScenarios");

        Assert.Equal(
            "[Atlas] staged mod 'mymod' for My.Scenarios.PlayerScenarios: verified " +
            "(MVID 11111111-1111-1111-1111-111111111111, loaded from '/tests/bin/Mod.dll')",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_NameTheClassOnEverySkippedLine_When_AnOwnerIsGiven()
    {
        const string owner = "My.Scenarios.PlayerScenarios";

        Assert.Equal(
            "[Atlas] staged mod 'mymod' for My.Scenarios.PlayerScenarios: skipped, not a code mod (no ModSystem was loaded from it)",
            StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], loaded: null, owner).Text);
        Assert.Equal(
            "[Atlas] staged mod 'mymod' for My.Scenarios.PlayerScenarios: skipped, no staged dll at its root (a source mod, compiled by the engine)",
            StagedModBinding.Verify("mymod", [], Loaded("Mod", BoundMvid), owner).Text);
        Assert.Equal(
            "[Atlas] staged mod 'mymod' for My.Scenarios.PlayerScenarios: skipped, no staged dll is named 'Mod', the assembly the engine bound",
            StagedModBinding.Verify("mymod", [Staged("Dependency", StagedMvid)], Loaded("Mod", BoundMvid), owner).Text);
    }

    [Fact]
    public void Verify_Should_SayAnEarlierBootBoundTheCopy_When_ItIsNotThisBootsStagedOne()
    {
        // The assembly is bound once per process: for every boot after the first, the path is the
        // first boot's scratch folder, which may be deleted by now.
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod",
            [Staged("Mod", StagedMvid, "/scratch/second/TestMods/Mod.dll")],
            Loaded("Mod", StagedMvid, "/scratch/first/TestMods/Mod.dll"),
            "My.Scenarios.PlayerScenarios",
            boundByEarlierBoot: true);

        Assert.False(verdict.Mismatch);
        Assert.Equal(
            "[Atlas] staged mod 'mymod' for My.Scenarios.PlayerScenarios: verified (MVID 11111111-1111-1111-1111-111111111111, " +
            "loaded from '/scratch/first/TestMods/Mod.dll', bound by an earlier boot of this process, so that path may be gone)",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_NameTheInMemoryImage_When_TheVerifiedAssemblyHasNoFile()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", StagedMvid, StagedModBinding.InMemoryImage));

        Assert.Contains("loaded from '<in-memory image>')", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_ReportVerified_When_TheBoundAssemblyIsACopyOfTheStagedBuildAtAnotherPath()
    {
        // The ordinary layout: a ProjectReference put the mod's build next to the test assembly
        // and the same build is staged from elsewhere. Only content counts, never the path.
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod",
            [Staged("Mod", StagedMvid, "/scratch/TestMods/Mod.dll")],
            Loaded("Mod", StagedMvid, "/tests/bin/Release/net10.0/Mod.dll"));

        Assert.False(verdict.Mismatch);
        Assert.Contains("verified (MVID " + StagedMvid + ", loaded from '/tests/bin/Release/net10.0/Mod.dll')", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_DescribeTheMismatch_When_TheBoundAssemblyIsAnotherBuildOfTheSameName()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod",
            [Staged("Mod", StagedMvid, "/scratch/TestMods/Mod.dll")],
            Loaded("Mod", BoundMvid, "/tests/bin/Release/net10.0/Mod.dll"));

        Assert.True(verdict.Mismatch);
        Assert.Contains("'mymod'", verdict.Text, StringComparison.Ordinal);
        Assert.Contains("'/scratch/TestMods/Mod.dll'", verdict.Text, StringComparison.Ordinal);
        Assert.Contains(StagedMvid.ToString(), verdict.Text, StringComparison.Ordinal);
        Assert.Contains("'/tests/bin/Release/net10.0/Mod.dll'", verdict.Text, StringComparison.Ordinal);
        Assert.Contains(BoundMvid.ToString(), verdict.Text, StringComparison.Ordinal);
        Assert.Contains("another build", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_PointAtTheFix_When_ItDescribesAMismatch()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.True(verdict.Mismatch);
        Assert.Contains("one build of the mod per test project", verdict.Text, StringComparison.Ordinal);
        Assert.Contains("/wiki/Mod-Staging#testing-two-builds-of-the-same-mod", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_SkipWithTheBoundName_When_NoStagedFileSharesTheBoundAssemblysName()
    {
        // The bound assembly did not come from this staging at all (or the staged mod ships only
        // dependencies): a different name is not two builds of one identity.
        StagedModBinding.Verdict verdict = StagedModBinding.Verify(
            "mymod", [Staged("Dependency", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.False(verdict.Mismatch);
        Assert.Equal(
            "[Atlas] staged mod 'mymod': skipped, no staged dll is named 'Mod', the assembly the engine bound",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_SkipAsASourceMod_When_TheStagedModShipsNoManagedDll()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [], Loaded("Mod", BoundMvid));

        Assert.False(verdict.Mismatch);
        Assert.Equal(
            "[Atlas] staged mod 'mymod': skipped, no staged dll at its root (a source mod, compiled by the engine)",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_SkipAsNotACodeMod_When_NoAssemblyWasBound()
    {
        // A content-only mod has no ModSystem, so there is no bound assembly to name; what it
        // ships is beside the point.
        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], loaded: null);

        Assert.False(verdict.Mismatch);
        Assert.Equal(
            "[Atlas] staged mod 'mymod': skipped, not a code mod (no ModSystem was loaded from it)",
            verdict.Text);
    }

    [Fact]
    public void Verify_Should_CompareNamesIgnoringCase_When_TheStagedFileSpellsItDifferently()
    {
        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [Staged("MOD", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.True(verdict.Mismatch);
    }

    [Fact]
    public void Verify_Should_NameTheSameNamedFile_When_ADependencyDllIsStagedBesideIt()
    {
        StagedModBinding.AssemblyFile dependency = Staged("Dependency", BoundMvid, "/scratch/TestMods/mymod/Dependency.dll");
        StagedModBinding.AssemblyFile mod = Staged("Mod", StagedMvid, "/scratch/TestMods/mymod/Mod.dll");

        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [dependency, mod], Loaded("Mod", BoundMvid));

        Assert.True(verdict.Mismatch);
        Assert.Contains("/scratch/TestMods/mymod/Mod.dll", verdict.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dependency.dll", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_ReportVerified_When_ADependencyDllHappensToCarryTheBoundMvidUnderAnotherName()
    {
        // Names gate the comparison: a same-MVID file of another name proves nothing about the
        // same-named mod dll, which here matches on its own.
        StagedModBinding.AssemblyFile dependency = Staged("Dependency", BoundMvid);
        StagedModBinding.AssemblyFile mod = Staged("Mod", BoundMvid);

        StagedModBinding.Verdict verdict = StagedModBinding.Verify("mymod", [dependency, mod], Loaded("Mod", BoundMvid));

        Assert.False(verdict.Mismatch);
        Assert.Contains("verified (MVID " + BoundMvid + ",", verdict.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_NameTheInMemoryImage_When_TheBoundAssemblyHasNoFile()
    {
        string message = StagedModBinding.Describe(
            "mymod",
            Staged("Mod", StagedMvid),
            Loaded("Mod", BoundMvid, StagedModBinding.InMemoryImage));

        Assert.Contains("'<in-memory image>'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_NameTheStagedFileAndTheLoadedAssembly_When_TheEngineRefusedTheStagedBuild()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll",
            [Staged("Mod", StagedMvid, "/repo/beta/Mod.dll")],
            [SameNameError("Mod")],
            name => name == "Mod" ? Loaded("Mod", BoundMvid, "/repo/alpha/Mod.dll") : null);

        Assert.NotNull(message);
        Assert.Contains("'mymod'", message, StringComparison.Ordinal);
        Assert.Contains("staged from '/repo/beta/Mod.dll' (MVID " + StagedMvid + ")", message, StringComparison.Ordinal);
        Assert.Contains("refused", message, StringComparison.Ordinal);
        Assert.Contains("'/repo/alpha/Mod.dll' (MVID " + BoundMvid + ")", message, StringComparison.Ordinal);
        Assert.Contains("/wiki/Mod-Staging#testing-two-builds-of-the-same-mod", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_SayTheModIsAbsent_When_TheEngineRefusedTheStagedBuild()
    {
        // The boot is green and the mod is simply not there: the message has to say that, since
        // it is the one thing the engine's own error does not.
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod")], _ => Loaded("Mod", BoundMvid));

        Assert.NotNull(message);
        Assert.Contains("booted on without the mod", message, StringComparison.Ordinal);
        Assert.Contains("absent from this world", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_NotRepeatTheEnginesRestartAdvice_When_ItWordsTheError()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod")], _ => Loaded("Mod", BoundMvid));

        Assert.DoesNotContain("restart the game", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeRefusal_Should_NameTheModByTheEnginesHint_When_TheErrorCarriesOne()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod", hint: "theirmodid")], _ => Loaded("Mod", BoundMvid));

        Assert.Contains("Mod 'theirmodid'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_NameTheModByItsFileName_When_TheErrorCarriesNoHint()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod", hint: null)], _ => Loaded("Mod", BoundMvid));

        Assert.Contains("Mod 'Mod.dll'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_StillName_When_NoAssemblyOfThatNameIsLoadedAnyMore()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod")], _ => null);

        Assert.NotNull(message);
        Assert.Contains("an assembly named 'Mod' is already loaded", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheEngineLoggedNothingAboutTheAssembly()
    {
        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheEngineErrorIsAboutAnotherFailure()
    {
        // A staged mod meant to fail loading (a dependency dll staged alone, a dll with no
        // ModSystem) has its own error; only the same-name refusal is this check's business.
        BootDiagnosticEntry other = Error(
            "Exception: Mod.dll declared as code mod, but there are no .dll files that contain at least one ModSystem or has a ModInfo attribute");

        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [other], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheRefusalNamesAnotherAssembly()
    {
        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Library")], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_NotMatchAnAssemblyWhoseNameOnlyStartsLikeIt_When_TheNamesDiffer()
    {
        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("ModExtras")], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_MatchTheNameIgnoringCase_When_TheEngineSpellsItDifferently()
    {
        Assert.NotNull(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("MOD")], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheLoadedAssemblyIsTheStagedBuild()
    {
        // Same content already loaded: nothing was refused on this file's account, whatever
        // else the log says.
        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [SameNameError("Mod")], _ => Loaded("Mod", StagedMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheSameNameLineIsOnlyAWarning()
    {
        BootDiagnosticEntry warning = SameNameError("Mod") with { Level = EnumLogType.Warning };

        Assert.Null(StagedModBinding.DescribeRefusal(
            "Mod.dll", [Staged("Mod", StagedMvid)], [warning], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void DescribeRefusal_Should_PickTheFileTheErrorNames_When_ADependencyDllIsStagedBesideIt()
    {
        string? message = StagedModBinding.DescribeRefusal(
            "mymod",
            [Staged("Dependency", BoundMvid, "/repo/out/Dependency.dll"), Staged("Mod", StagedMvid, "/repo/out/Mod.dll")],
            [SameNameError("Mod")],
            name => Loaded(name, BoundMvid, "/tests/bin/" + name + ".dll"));

        Assert.NotNull(message);
        Assert.Contains("'/repo/out/Mod.dll'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Dependency.dll", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_Should_ReturnNull_When_TheStagedModShipsNoManagedDll()
    {
        Assert.Null(StagedModBinding.DescribeRefusal("Mod.dll", [], [SameNameError("Mod")], _ => Loaded("Mod", BoundMvid)));
    }

    [Fact]
    public void ToSourcePath_Should_ReturnTheSource_When_TheStagedFileIsTheStagedModItself()
    {
        string path = StagedModBinding.ToSourcePath("/scratch/TestMods/Mod.dll", "/scratch/TestMods/Mod.dll", "/repo/bin/Mod.dll");

        Assert.Equal("/repo/bin/Mod.dll", path);
    }

    [Fact]
    public void ToSourcePath_Should_KeepTheRelativePart_When_TheStagedFileIsInsideAStagedFolder()
    {
        string path = StagedModBinding.ToSourcePath(
            Path.Combine("/scratch/TestMods/mymod", "Mod.dll"), "/scratch/TestMods/mymod", "/repo/out/mymod");

        Assert.Equal("/repo/out/mymod" + Path.DirectorySeparatorChar + "Mod.dll", path);
    }

    [Fact]
    public void ToSourcePath_Should_KeepTheEntryName_When_TheStagedFileIsInsideAStagedZip()
    {
        string path = StagedModBinding.ToSourcePath(
            "/scratch/TestMods/mymod.zip!/Mod.dll", "/scratch/TestMods/mymod.zip", "/repo/out/mymod.zip");

        Assert.Equal("/repo/out/mymod.zip!/Mod.dll", path);
    }

    [Fact]
    public void ToSourcePath_Should_ReturnTheStagedPath_When_ItIsNotUnderTheStagedRoot()
    {
        // A sibling that merely starts with the same characters is another mod.
        string path = StagedModBinding.ToSourcePath(
            Path.Combine("/scratch/TestMods/mymod2", "Mod.dll"), "/scratch/TestMods/mymod", "/repo/out/mymod");

        Assert.Equal(Path.Combine("/scratch/TestMods/mymod2", "Mod.dll"), path);
    }

    // What the engine logs, as boot diagnostics record it, when it refuses a second build: the
    // FileLoadException of Assembly.UnsafeLoadFrom, measured on 1.21.7, 1.22.3 and 1.22.7.
    private static BootDiagnosticEntry SameNameError(string assemblyName, string? hint = "mymod")
        => Error(
            $"Exception: Could not load file or assembly '{assemblyName}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. " +
            "Assembly with same name is already loaded\n   at System.Runtime.Loader.AssemblyLoadContext.LoadFromPath(IntPtr ptrNativeAssemblyBinder, String ilPath, String niPath, ObjectHandleOnStack retAssembly)",
            hint);

    private static BootDiagnosticEntry Error(string message, string? hint = "mymod")
        => new(EnumLogType.Error, "unknown", message, null, hint);

    private static StagedModBinding.AssemblyFile Staged(string name, Guid mvid, string path = "/scratch/TestMods/Mod.dll")
        => new(path, name, mvid);

    private static StagedModBinding.AssemblyFile Loaded(string name, Guid mvid, string path = "/tests/bin/Mod.dll")
        => new(path, name, mvid);
}
