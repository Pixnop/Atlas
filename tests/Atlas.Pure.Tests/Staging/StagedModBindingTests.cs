using Atlas.Internal.Staging;

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
        Assert.Equal("[Atlas] staged mod 'mymod': verified (MVID 11111111-1111-1111-1111-111111111111)", verdict.Text);
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
        Assert.Contains("verified (MVID " + StagedMvid + ")", verdict.Text, StringComparison.Ordinal);
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
        Assert.Contains("verified (MVID " + BoundMvid + ")", verdict.Text, StringComparison.Ordinal);
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

    private static StagedModBinding.AssemblyFile Staged(string name, Guid mvid, string path = "/scratch/TestMods/Mod.dll")
        => new(path, name, mvid);

    private static StagedModBinding.AssemblyFile Loaded(string name, Guid mvid, string path = "/tests/bin/Mod.dll")
        => new(path, name, mvid);
}
