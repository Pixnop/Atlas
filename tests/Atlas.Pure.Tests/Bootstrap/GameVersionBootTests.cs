using Atlas.Api;
using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class GameVersionBootTests
{
    // The announced lines are remembered for the process, so every test names an install of its
    // own: two tests never share a line.
    private static int _counter;

    [Fact]
    public void Describe_Should_NameTheInstallAndBothVersions_When_TheyDiffer()
    {
        string line = GameVersionBoot.Describe("1.22.3", "/opt/vs/1.22.3", "1.22.7");

        Assert.Equal("[Atlas] game 1.22.3 from '/opt/vs/1.22.3' (scenarios compiled against 1.22.7)", line);
    }

    [Fact]
    public void Describe_Should_NameBothVersions_When_TheyAreTheSame()
    {
        string line = GameVersionBoot.Describe("1.21.7", "/opt/vs/1.21.7", "1.21.7");

        Assert.Equal("[Atlas] game 1.21.7 from '/opt/vs/1.21.7' (scenarios compiled against 1.21.7)", line);
    }

    [Fact]
    public void Describe_Should_SayTheAssemblyCarriesNoVersion_When_NoneWasStamped()
    {
        string line = GameVersionBoot.Describe("1.22.3", "/opt/vs/1.22.3", compiledVersion: null);

        Assert.StartsWith("[Atlas] game 1.22.3 from '/opt/vs/1.22.3' (", line, StringComparison.Ordinal);
        Assert.Contains("no compiled game version", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_Should_WriteTheLineOnce_When_CalledForEveryBootOfTheProcess()
    {
        string install = UniqueInstall();
        var written = new List<string>();

        GameVersionBoot.Check("1.22.3", install, new CompiledGameVersion("1.22.7", Required: false), written.Add);
        GameVersionBoot.Check("1.22.3", install, new CompiledGameVersion("1.22.7", Required: false), written.Add);
        GameVersionBoot.Check("1.22.3", install, new CompiledGameVersion("1.22.7", Required: false), written.Add);

        Assert.Equal([$"[Atlas] game 1.22.3 from '{install}' (scenarios compiled against 1.22.7)"], written);
    }

    [Fact]
    public void Check_Should_WriteAnotherLine_When_TheCompiledVersionChanges()
    {
        // A process can run two scenario assemblies built against different games (atlas run takes
        // one, a test host can hold several): each distinct pair is worth its own line.
        string install = UniqueInstall();
        var written = new List<string>();

        GameVersionBoot.Check("1.22.3", install, new CompiledGameVersion("1.22.7", Required: false), written.Add);
        GameVersionBoot.Check("1.22.3", install, new CompiledGameVersion("1.21.7", Required: false), written.Add);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void Check_Should_WriteTheLine_When_NoCompiledVersionIsKnownAtAll()
    {
        // A host no scenario class owns (the engine tests build some) has no recipe to read.
        string install = UniqueInstall();
        var written = new List<string>();

        GameVersionBoot.Check("1.22.3", install, compiled: null, written.Add);

        Assert.Contains("no compiled game version", Assert.Single(written), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_Should_NotThrow_When_TheVersionsDifferAndNothingIsRequired()
    {
        // The cross-install run (issue #49): a build made against one game on another install.
        Exception? thrown = Record.Exception(() => GameVersionBoot.Check(
            "1.21.7", UniqueInstall(), new CompiledGameVersion("1.22.3", Required: false), _ => { }));

        Assert.Null(thrown);
    }

    [Fact]
    public void Check_Should_NotThrow_When_TheVersionsMatchAndTheyAreRequired()
    {
        Exception? thrown = Record.Exception(() => GameVersionBoot.Check(
            "1.22.3", UniqueInstall(), new CompiledGameVersion("1.22.3", Required: true), _ => { }));

        Assert.Null(thrown);
    }

    [Fact]
    public void Check_Should_ThrowNamingBothVersionsAndTheInstall_When_TheyDifferAndAreRequired()
    {
        string install = UniqueInstall();

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check(
            "1.22.3", install, new CompiledGameVersion("1.22.7", Required: true), _ => { }));

        Assert.Contains("1.22.7", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1.22.3", ex.Message, StringComparison.Ordinal);
        Assert.Contains(install, ex.Message, StringComparison.Ordinal);
        Assert.Contains("AtlasRequireCompiledGameVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_Should_ThrowOnEveryBoot_When_TheyDifferAndAreRequired()
    {
        // The line is once per process; the refusal is not: every boot of the class fails.
        string install = UniqueInstall();
        var compiled = new CompiledGameVersion("1.22.7", Required: true);

        Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check("1.22.3", install, compiled, _ => { }));
        Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check("1.22.3", install, compiled, _ => { }));
    }

    [Fact]
    public void Check_Should_WriteTheLineBeforeThrowing_When_TheyDifferAndAreRequired()
    {
        string install = UniqueInstall();
        var written = new List<string>();

        Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check(
            "1.22.3", install, new CompiledGameVersion("1.22.7", Required: true), written.Add));

        Assert.Single(written);
    }

    [Fact]
    public void Check_Should_Throw_When_ARequiredAssemblyCarriesNoVersion()
    {
        // The attribute asks for a guarantee nothing can check: refusing is the safe answer, and
        // the message says why it cannot be checked.
        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check(
            "1.22.3", UniqueInstall(), new CompiledGameVersion(null, Required: true), _ => { }));

        Assert.Contains("no compiled game version", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AtlasRequireCompiledGameVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_Should_CompareTheVersionsExactly_When_ThePatchDiffers()
    {
        // 1.22.3 against 1.22.7 is the case this exists for: the same minor, a different patch.
        Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check(
            "1.22.3", UniqueInstall(), new CompiledGameVersion("1.22.7", Required: true), _ => { }));
        Assert.Throws<AtlasSetupException>(() => GameVersionBoot.Check(
            "1.22.0-rc.1", UniqueInstall(), new CompiledGameVersion("1.22.0", Required: true), _ => { }));
    }

    private static string UniqueInstall() => $"/opt/vs/test-{Interlocked.Increment(ref _counter)}";
}
