using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class StagedModBindingTests
{
    private static readonly Guid StagedMvid = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BoundMvid = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Verify_Should_ReturnNull_When_TheBoundAssemblyIsTheStagedBuild()
    {
        string? error = StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", StagedMvid));

        Assert.Null(error);
    }

    [Fact]
    public void Verify_Should_ReturnNull_When_TheBoundAssemblyIsACopyOfTheStagedBuildAtAnotherPath()
    {
        // The ordinary layout: a ProjectReference put the mod's build next to the test assembly
        // and the same build is staged from elsewhere. Only content counts, never the path.
        string? error = StagedModBinding.Verify(
            "mymod",
            [Staged("Mod", StagedMvid, "/scratch/TestMods/Mod.dll")],
            Loaded("Mod", StagedMvid, "/tests/bin/Release/net10.0/Mod.dll"));

        Assert.Null(error);
    }

    [Fact]
    public void Verify_Should_DescribeTheMismatch_When_TheBoundAssemblyIsAnotherBuildOfTheSameName()
    {
        string? error = StagedModBinding.Verify(
            "mymod",
            [Staged("Mod", StagedMvid, "/scratch/TestMods/Mod.dll")],
            Loaded("Mod", BoundMvid, "/tests/bin/Release/net10.0/Mod.dll"));

        Assert.NotNull(error);
        Assert.Contains("'mymod'", error, StringComparison.Ordinal);
        Assert.Contains("'/scratch/TestMods/Mod.dll'", error, StringComparison.Ordinal);
        Assert.Contains(StagedMvid.ToString(), error, StringComparison.Ordinal);
        Assert.Contains("'/tests/bin/Release/net10.0/Mod.dll'", error, StringComparison.Ordinal);
        Assert.Contains(BoundMvid.ToString(), error, StringComparison.Ordinal);
        Assert.Contains("another build", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_PointAtTheFix_When_ItDescribesAMismatch()
    {
        string? error = StagedModBinding.Verify("mymod", [Staged("Mod", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.NotNull(error);
        Assert.Contains("one build of the mod per test project", error, StringComparison.Ordinal);
        Assert.Contains("/wiki/Mod-Staging#testing-two-builds-of-the-same-mod", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_ReturnNull_When_NoStagedFileSharesTheBoundAssemblysName()
    {
        // The bound assembly did not come from this staging at all (or the staged mod ships only
        // dependencies): a different name is not two builds of one identity.
        string? error = StagedModBinding.Verify(
            "mymod", [Staged("Dependency", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.Null(error);
    }

    [Fact]
    public void Verify_Should_ReturnNull_When_TheStagedModShipsNoManagedDll()
    {
        string? error = StagedModBinding.Verify("mymod", [], Loaded("Mod", BoundMvid));

        Assert.Null(error);
    }

    [Fact]
    public void Verify_Should_CompareNamesIgnoringCase_When_TheStagedFileSpellsItDifferently()
    {
        string? error = StagedModBinding.Verify("mymod", [Staged("MOD", StagedMvid)], Loaded("Mod", BoundMvid));

        Assert.NotNull(error);
    }

    [Fact]
    public void Verify_Should_NameTheSameNamedFile_When_ADependencyDllIsStagedBesideIt()
    {
        StagedModBinding.AssemblyFile dependency = Staged("Dependency", BoundMvid, "/scratch/TestMods/mymod/Dependency.dll");
        StagedModBinding.AssemblyFile mod = Staged("Mod", StagedMvid, "/scratch/TestMods/mymod/Mod.dll");

        string? error = StagedModBinding.Verify("mymod", [dependency, mod], Loaded("Mod", BoundMvid));

        Assert.NotNull(error);
        Assert.Contains("/scratch/TestMods/mymod/Mod.dll", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Dependency.dll", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Should_ReturnNull_When_ADependencyDllHappensToCarryTheBoundMvidUnderAnotherName()
    {
        // Names gate the comparison: a same-MVID file of another name proves nothing about the
        // same-named mod dll, which here matches on its own.
        StagedModBinding.AssemblyFile dependency = Staged("Dependency", BoundMvid);
        StagedModBinding.AssemblyFile mod = Staged("Mod", BoundMvid);

        Assert.Null(StagedModBinding.Verify("mymod", [dependency, mod], Loaded("Mod", BoundMvid)));
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

    private static StagedModBinding.AssemblyFile Staged(string name, Guid mvid, string path = "/scratch/TestMods/Mod.dll")
        => new(path, name, mvid);

    private static StagedModBinding.AssemblyFile Loaded(string name, Guid mvid, string path = "/tests/bin/Mod.dll")
        => new(path, name, mvid);
}
