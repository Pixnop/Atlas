using System.Diagnostics;
using System.Runtime.Versioning;
using Atlas.Internal.RealClient;

namespace Atlas.Pure.Tests.RealClient;

/// <summary>Contract of the real shell under the availability ladder: what each probe reads from
/// the machine, and how the namespace probe reports its outcome. The namespace probe is driven with
/// fake <c>unshare</c> programs (small shell scripts), so no namespace is created and nothing of the
/// sandbox runs; the probes that read the machine are Linux and Unix shell behaviour, so the tests
/// that need a shell return early elsewhere (as the other Unix-only pure tests do).</summary>
[UnsupportedOSPlatform("windows")]
public sealed class ClientProbesTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("atlas-probes").FullName;

    public void Dispose()
    {
        Directory.Delete(_scratch, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OfThisMachine_Should_ReadThisProcessEnvironmentAndPlatform_When_Asked()
    {
        ClientProbes probes = ClientProbes.OfThisMachine();

        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), probes.Env("PATH"));
        Assert.Null(probes.Env("ATLAS_PROBE_TEST_UNSET_VARIABLE"));
        Assert.Equal(OperatingSystem.IsLinux(), probes.IsLinux);
    }

    [Fact]
    public void ReadText_Should_ReturnTheContent_When_TheFileIsThere()
    {
        string file = Path.Combine(_scratch, "status");
        File.WriteAllText(file, "Uid:\t1000");

        Assert.Equal("Uid:\t1000", ClientProbes.OfThisMachine().ReadText(file));
    }

    [Fact]
    public void ReadText_Should_ReturnNull_When_TheFileIsMissingOrIsNotAFile()
    {
        ClientProbes probes = ClientProbes.OfThisMachine();

        Assert.Null(probes.ReadText(Path.Combine(_scratch, "missing")));
        Assert.Null(probes.ReadText(_scratch));
    }

    [Fact]
    public void FileExists_Should_TellAFileFromAFolderAndFromNothing_When_Asked()
    {
        string file = Path.Combine(_scratch, "present");
        File.WriteAllText(file, string.Empty);
        ClientProbes probes = ClientProbes.OfThisMachine();

        Assert.True(probes.FileExists(file));
        Assert.False(probes.FileExists(_scratch));
        Assert.False(probes.FileExists(Path.Combine(_scratch, "missing")));
    }

    [Fact]
    public void ListFolders_Should_ReturnTheNamesOfTheSubFoldersOnly_When_TheFolderExists()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "1.22.3"));
        Directory.CreateDirectory(Path.Combine(_scratch, "1.21.7"));
        File.WriteAllText(Path.Combine(_scratch, "notes.txt"), string.Empty);

        IEnumerable<string> names = ClientProbes.OfThisMachine().ListFolders(_scratch);

        Assert.Equal(["1.21.7", "1.22.3"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ListFolders_Should_ReturnNothing_When_TheFolderIsMissing()
        => Assert.Empty(ClientProbes.OfThisMachine().ListFolders(Path.Combine(_scratch, "missing")));

    [Fact]
    public void FindTool_Should_ReturnTheFullPathOfAProgramOnPath_And_NullForAnUnknownOne()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ClientProbes probes = ClientProbes.OfThisMachine();

        string found = Assert.IsType<string>(probes.FindTool("sh"));
        Assert.True(Path.IsPathRooted(found));
        Assert.True(File.Exists(found));
        Assert.Equal("sh", Path.GetFileName(found));
        Assert.Null(probes.FindTool("atlas-no-such-tool-4f2a"));
    }

    [Fact]
    public void ProbeNamespaces_Should_ReturnNull_When_UnshareSucceeds()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("exit 0");

        Assert.Null(ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ProbeNamespaces_Should_AskForTheNamespacesOfTheRealLaunchAndTheCapabilityDrop_When_Run()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("exit 0");

        ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30));

        // One argument per line: the flags of the launch, then bash -c <script> <setpriv>.
        string[] args = File.ReadAllLines(unshare + ".args");
        string[] flags = [.. SandboxPlan.NamespaceFlags(isolateNetwork: false)];
        Assert.Equal(flags, args[..flags.Length]);
        Assert.Equal(["bash", "-c"], args[flags.Length..(flags.Length + 2)]);
        string script = args[flags.Length + 2];
        Assert.Contains("mount -t tmpfs tmpfs /tmp", script);
        Assert.Contains(string.Join(' ', SandboxPlan.CapabilityDropFlags), script);
        Assert.Equal("/fake/setpriv", args[^1]);
        Assert.Equal(flags.Length + 4, args.Length);
    }

    [Fact]
    public void ProbeNamespaces_Should_CloseTheProbeStdin_When_Run()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // A program that reads its stdin to the end would hold the probe for the whole timeout if
        // the probe kept the pipe open.
        string unshare = FakeUnshare("cat > /dev/null");

        Assert.Null(ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ProbeNamespaces_Should_ReturnTheFirstLineOfTheError_When_UnshareFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("echo 'unshare: write failed /proc/self/uid_map: Operation not permitted' >&2\necho 'a second line' >&2\nexit 1");

        string? reason = ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30));

        Assert.Equal("unshare: write failed /proc/self/uid_map: Operation not permitted", reason);
    }

    [Fact]
    public void ProbeNamespaces_Should_ReturnTheExitCode_When_UnsharePrintsNothingAndFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("exit 3");

        Assert.Equal("the probe exited with code 3", ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ProbeNamespaces_Should_StopWaitingAndSayHowLong_When_UnshareNeverEnds()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("exec sleep 60");
        var clock = Stopwatch.StartNew();

        string? reason = ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromMilliseconds(300));

        Assert.Equal("the probe did not finish in 0.3 seconds", reason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the probe was killed, not waited out");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/atlas/no/such/unshare")]
    public void ProbeNamespaces_Should_ReturnTheReason_When_UnshareCannotBeStarted(string unshare)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? reason = ClientProbes.ProbeNamespacesWith(unshare, "/fake/setpriv", TimeSpan.FromSeconds(30));

        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void OfThisMachine_Should_RunTheNamespaceProbeOnTheToolsItIsGiven_When_Asked()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string unshare = FakeUnshare("exit 5");

        Assert.Equal("the probe exited with code 5", ClientProbes.OfThisMachine().ProbeNamespaces(unshare, "/fake/setpriv"));
    }

    // A program that records its arguments, one per line, next to itself and then runs the body.
    private string FakeUnshare(string body)
    {
        string path = Path.Combine(_scratch, "unshare-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$0.args\"\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
