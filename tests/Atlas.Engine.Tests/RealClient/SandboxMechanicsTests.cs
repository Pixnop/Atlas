using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Atlas.Internal.RealClient;
using Xunit.Abstractions;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>The sandbox's mechanics (environment, mounts, core limit, ceiling, guardian, stop
/// ladder, launcher death) proven with a harmless stand-in program run in the real sandbox, so
/// none of this needs the 3 GB game client, nor even a client install: they run against a
/// server-only install as well. They need the sandbox's tools and user namespaces, so they are
/// skipped with the reason wherever the sandbox cannot run, and they carry the
/// <c>AtlasClient</c> trait, not <c>E2E</c>: no CI shard filter selects them. Run them with
/// <c>--filter "Category=AtlasClient"</c>. The stand-in never sees a game data path: the one it
/// is given is a folder it does not touch.</summary>
[Trait("Category", "AtlasClient")]
[UnsupportedOSPlatform("windows")]
public class SandboxMechanicsTests
{
    // What bash adds to the environment of a script it starts, on top of what it was given.
    private static readonly string[] BashArtifacts = ["_", "PWD", "SHLVL", "OLDPWD"];

    private readonly ITestOutputHelper _output;

    public SandboxMechanicsTests(ITestOutputHelper output) => _output = output;

    [SandboxFact]
    public async Task Client_Should_GetTheWhitelistAndThePrivateDisplayAndNothingElse_When_Started()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Client_Should_GetTheWhitelistAndThePrivateDisplayAndNothingElse_When_Started));
        uint uid = HostUid();
        string standIn = run.StandIn(
            "env > \"$TMPDIR/standin.env\"\n" +
            "ulimit -c > \"$TMPDIR/standin.core\"\n" +
            "id -u > \"$TMPDIR/standin.uid\"\n" +
            "ls -A /tmp > \"$TMPDIR/standin.tmp\"\n" +
            "ls -A /tmp/.X11-unix > \"$TMPDIR/standin.x11\"\n" +
            $"ls -A /run/user/{uid} > \"$TMPDIR/standin.runuser\" 2>&1");
        string[] hostSockets = RealClientEnvironment.HostX11Sockets();

        await using ClientSandbox sandbox = ClientSandbox.Start(run.Options(standIn), run.StandInToolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        string Fact(string name) => File.ReadAllText(Path.Combine(sandbox.Plan.Tmp, "standin." + name));
        Dictionary<string, string> env = Fact("env")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(pair => !BashArtifacts.Contains(pair[0]))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        string display = Assert.Contains("DISPLAY", env);
        Assert.Matches(@"^:\d+$", display);
        Assert.NotEqual(Environment.GetEnvironmentVariable("DISPLAY"), display);
        env.Remove("DISPLAY");
        Assert.Equal(sandbox.Plan.ClientEnvironment.OrderBy(p => p.Key, StringComparer.Ordinal), env.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal("0", Fact("core").Trim());
        Assert.Equal("0", Fact("uid").Trim());
        Assert.Equal(".X11-unix", Fact("tmp").Trim());
        Assert.Equal("X" + display[1..], Fact("x11").Trim());
        Assert.Empty(Fact("runuser").Trim());
        Assert.Equal(hostSockets, RealClientEnvironment.HostX11Sockets());
        Assert.Equal(display, ":" + File.ReadAllText(Path.Combine(run.RunDirectory, "display")).Trim());
    }

    [SandboxFact]
    public async Task Client_Should_HoldNoCapabilityAndBeUnableToUndoThePrivateMounts_When_Started()
    {
        // Root of the user namespace with its capabilities could umount the private tmpfs and
        // see the host's /tmp and /run/user/<uid> under them. Nothing in the sandbox may be able
        // to, so the stand-in tries every way it can.
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Client_Should_HoldNoCapabilityAndBeUnableToUndoThePrivateMounts_When_Started));
        uint uid = HostUid();
        string runUser = $"/run/user/{uid}";
        string standIn = run.StandIn(
            "grep -E '^(Cap(Inh|Prm|Eff|Bnd|Amb)|NoNewPrivs):' /proc/self/status > \"$TMPDIR/standin.caps\"\n" +
            "umount /tmp > \"$TMPDIR/standin.umount-tmp\" 2>&1; echo $? > \"$TMPDIR/standin.umount-tmp.rc\"\n" +
            $"umount {runUser} > \"$TMPDIR/standin.umount-runuser\" 2>&1; echo $? > \"$TMPDIR/standin.umount-runuser.rc\"\n" +
            "unshare --user --map-root-user --mount umount /tmp > \"$TMPDIR/standin.nested\" 2>&1; echo $? > \"$TMPDIR/standin.nested.rc\"\n" +
            "ls -A /tmp > \"$TMPDIR/standin.tmp-after\"\n" +
            $"ls -A {runUser} > \"$TMPDIR/standin.runuser-after\" 2>&1");

        await using ClientSandbox sandbox = ClientSandbox.Start(run.Options(standIn), run.StandInToolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        string Fact(string name) => File.ReadAllText(Path.Combine(sandbox.Plan.Tmp, "standin." + name));
        string[] caps = Fact("caps").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, caps.Length);
        Assert.All(caps.Where(line => line.StartsWith("Cap", StringComparison.Ordinal)), line => Assert.Matches(@"^Cap\w+:\s+0+$", line));
        Assert.Matches(@"^NoNewPrivs:\s+1$", Assert.Single(caps, line => line.StartsWith("NoNewPrivs", StringComparison.Ordinal)));
        Assert.NotEqual("0", Fact("umount-tmp.rc").Trim());
        Assert.Contains("superuser", Fact("umount-tmp"));
        Assert.NotEqual("0", Fact("umount-runuser.rc").Trim());
        Assert.Contains("superuser", Fact("umount-runuser"));
        Assert.NotEqual("0", Fact("nested.rc").Trim());
        Assert.Equal(".X11-unix", Fact("tmp-after").Trim());
        Assert.Empty(Fact("runuser-after").Trim());
        Assert.Contains("no capabilities", File.ReadAllText(sandbox.Plan.SandboxLog));
        _output.WriteLine($"umount /tmp: {Fact("umount-tmp").Trim()}; nested user namespace: {Fact("nested").Trim()}");
    }

    [SandboxFact]
    public async Task Sandbox_Should_RefuseToStartTheClient_When_TheCapabilitiesCannotBeDropped()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_RefuseToStartTheClient_When_TheCapabilitiesCannotBeDropped));
        string standIn = run.StandIn("touch \"$TMPDIR/client-ran\"");

        await using ClientSandbox sandbox = ClientSandbox.Start(
            run.Options(standIn), run.StandInToolchain with { SetprivPath = "/bin/false" });
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(70, sandbox.ExitCode);
        Assert.False(File.Exists(Path.Combine(sandbox.Plan.Tmp, "client-ran")));
        Assert.False(File.Exists(Path.Combine(run.RunDirectory, "display")), "no display may be started before the capabilities are gone");
        Assert.Contains("cannot drop the capabilities", File.ReadAllText(sandbox.Plan.SandboxLog));
    }

    [SandboxFact]
    public async Task Sandbox_Should_RefuseToStartTheClient_When_SetprivLeavesCapabilitiesBehind()
    {
        // A setpriv that accepts its options and drops nothing: the second stage checks its own
        // capability sets instead of trusting the tool.
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_RefuseToStartTheClient_When_SetprivLeavesCapabilitiesBehind));
        string fakeSetpriv = Path.Combine(run.Folder, "fake-setpriv.sh");
        File.WriteAllText(fakeSetpriv, "#!/bin/bash\nshift 4\nexec \"$@\"\n");
        File.SetUnixFileMode(fakeSetpriv, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string standIn = run.StandIn("touch \"$TMPDIR/client-ran\"");

        await using ClientSandbox sandbox = ClientSandbox.Start(
            run.Options(standIn), run.StandInToolchain with { SetprivPath = fakeSetpriv });
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(70, sandbox.ExitCode);
        Assert.False(File.Exists(Path.Combine(sandbox.Plan.Tmp, "client-ran")));
        Assert.False(File.Exists(Path.Combine(run.RunDirectory, "display")));
        Assert.Contains("stage 2 still holds capabilities", File.ReadAllText(sandbox.Plan.SandboxLog));
    }

    [SandboxFact]
    public async Task Client_Should_FindARelativeDataPathWhereTheHostMeantIt_When_TheInstallFolderIsEnteredFirst()
    {
        // The script changes into the install folder before it starts the client: the data path
        // reaches the stand-in as an absolute path of the host's working folder, and nothing
        // appears under the install.
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Client_Should_FindARelativeDataPathWhereTheHostMeantIt_When_TheInstallFolderIsEnteredFirst));
        string install = Directory.CreateDirectory(Path.Combine(run.Folder, "install")).FullName;
        string relativeData = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(run.Folder, "data"));
        string standIn = run.StandIn("printf '%s\\n' \"$@\" > \"$TMPDIR/standin.args\"\npwd > \"$TMPDIR/standin.pwd\"");
        ClientToolchain toolchain = run.StandInToolchain with { InstallDirectory = Path.GetRelativePath(Environment.CurrentDirectory, install), DataPath = relativeData };

        await using ClientSandbox sandbox = ClientSandbox.Start(run.Options(standIn), toolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        string[] args = File.ReadAllLines(Path.Combine(sandbox.Plan.Tmp, "standin.args"));
        Assert.Equal(["--dataPath", run.DataPath], args.Take(2));
        Assert.Equal(install, File.ReadAllText(Path.Combine(sandbox.Plan.Tmp, "standin.pwd")).Trim());
        Assert.Empty(Directory.EnumerateFileSystemEntries(install));
    }

    [SandboxFact]
    public async Task Client_Should_ReceiveTheDataPathLogPathAndExtraArgumentsInOrder_When_Started()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Client_Should_ReceiveTheDataPathLogPathAndExtraArgumentsInOrder_When_Started));
        string standIn = run.StandIn("printf '%s\\n' \"$@\" > \"$TMPDIR/standin.args\"");
        ClientSandboxOptions options = run.Options(standIn) with { Arguments = ["--connect", "127.0.0.1:4242", "--pw", "two words"] };

        await using ClientSandbox sandbox = ClientSandbox.Start(options, run.StandInToolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(
            ["--dataPath", run.DataPath, "--logPath", sandbox.Plan.Logs, "--connect", "127.0.0.1:4242", "--pw", "two words"],
            File.ReadAllLines(Path.Combine(sandbox.Plan.Tmp, "standin.args")));
    }

    [SandboxFact]
    public async Task Run_Should_LeaveTheDocumentedFolderLayoutAndScreenshots_When_Started()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Run_Should_LeaveTheDocumentedFolderLayoutAndScreenshots_When_Started));
        string standIn = run.StandIn("echo out; echo err >&2; exec sleep 600");
        ClientSandboxOptions options = run.Options(standIn) with { ScreenshotInterval = TimeSpan.FromSeconds(1) };

        await using ClientSandbox sandbox = ClientSandbox.Start(options, run.StandInToolchain);
        await RealClientEnvironment.EventuallyAsync(
            () => File.Exists(Path.Combine(run.RunDirectory, "client.stdout"))
                  && new FileInfo(Path.Combine(run.RunDirectory, "client.stdout")).Length >= 8,
            TimeSpan.FromSeconds(30),
            "the stand-in's output");
        if (RealClientEnvironment.SandboxPrograms.ImportPath is not null)
        {
            await RealClientEnvironment.EventuallyAsync(
                () => Directory.EnumerateFiles(sandbox.Plan.Screenshots, "*.png").Any(),
                TimeSpan.FromSeconds(30),
                "a screenshot of the private display");
        }

        foreach (string folder in new[] { sandbox.Plan.Logs, sandbox.Plan.Screenshots, sandbox.Plan.Home, sandbox.Plan.Tmp, sandbox.Plan.Xdg })
        {
            Assert.True(Directory.Exists(folder), folder);
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(run.RunDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(sandbox.Plan.Xdg));
        Assert.Equal("out\nerr\n", File.ReadAllText(sandbox.Plan.ClientStdout).ReplaceLineEndings("\n"));
        string log = File.ReadAllText(sandbox.Plan.SandboxLog);
        Assert.Contains("client start", log);
        Assert.Contains("display :", log);
    }

    [SandboxFact]
    public async Task IsolatedNetwork_Should_ShowOnlyALoopbackThatIsUp_When_Asked()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(IsolatedNetwork_Should_ShowOnlyALoopbackThatIsUp_When_Asked));
        string standIn = run.StandIn("ip -o link > \"$TMPDIR/standin.links\"");
        ClientSandboxOptions options = run.Options(standIn) with { IsolateNetwork = true };

        await using ClientSandbox sandbox = ClientSandbox.Start(options, run.StandInToolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        string[] links = File.ReadAllLines(Path.Combine(sandbox.Plan.Tmp, "standin.links"));
        string only = Assert.Single(links);
        Assert.Contains(": lo:", only);
        Assert.Contains("<LOOPBACK,UP,LOWER_UP>", only);
    }

    [SandboxFact]
    public async Task Ceiling_Should_StopTheClient_When_TheTimeoutPassesWithoutAnyHostAction()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Ceiling_Should_StopTheClient_When_TheTimeoutPassesWithoutAnyHostAction));
        ClientSandboxOptions options = run.Options(run.StandIn("exec sleep 600")) with
        {
            Timeout = TimeSpan.FromSeconds(3),
            StopGrace = TimeSpan.FromSeconds(2),
        };
        var clock = Stopwatch.StartNew();

        await using ClientSandbox sandbox = ClientSandbox.Start(options, run.StandInToolchain);
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.InRange(clock.Elapsed.TotalSeconds, 3, 12);
        Assert.Equal(124, sandbox.ExitCode);
        Assert.False(sandbox.StopRequested);
        Assert.Contains("client exit 124", File.ReadAllText(sandbox.Plan.SandboxLog));
    }

    [SandboxFact]
    public async Task Stop_Should_EndACooperativeClientThroughTheGuardian_When_Requested()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Stop_Should_EndACooperativeClientThroughTheGuardian_When_Requested));
        await using ClientSandbox sandbox = ClientSandbox.Start(run.Options(run.StandIn("exec sleep 600")), run.StandInToolchain);
        await WaitForClientStart(sandbox);
        var clock = Stopwatch.StartNew();

        await sandbox.StopAsync();

        Assert.True(sandbox.HasExited);
        Assert.True(sandbox.StopRequested);
        Assert.InRange(clock.Elapsed.TotalSeconds, 0, 5);
        Assert.Equal(143, sandbox.ExitCode);
        string log = File.ReadAllText(sandbox.Plan.SandboxLog);
        Assert.Contains("guardian: the host closed the pipe", log);
        await sandbox.StopAsync();
    }

    [SandboxFact]
    public async Task Stop_Should_KillAClientThatIgnoresSigtermAfterTheGrace_When_Requested()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Stop_Should_KillAClientThatIgnoresSigtermAfterTheGrace_When_Requested));
        string standIn = run.StandIn("trap '' TERM\ntouch \"$TMPDIR/ready\"\nwhile :; do sleep 1; done");
        ClientSandboxOptions options = run.Options(standIn) with { StopGrace = TimeSpan.FromSeconds(3) };
        await using ClientSandbox sandbox = ClientSandbox.Start(options, run.StandInToolchain);
        await RealClientEnvironment.EventuallyAsync(() => File.Exists(Path.Combine(sandbox.Plan.Tmp, "ready")), TimeSpan.FromSeconds(30), "the stand-in to ignore SIGTERM");
        IReadOnlyList<ProcessTable.Entry> tree = ProcessTable.Descendants(sandbox.LauncherPid);
        var clock = Stopwatch.StartNew();

        await sandbox.StopAsync();

        Assert.True(sandbox.HasExited);
        Assert.InRange(clock.Elapsed.TotalSeconds, 3, 3 + 5);
        Assert.Equal(137, sandbox.ExitCode);
        Assert.DoesNotContain(tree, ProcessTable.IsAlive);
    }

    [SandboxFact]
    public async Task Launcher_Should_TakeTheWholeNamespaceDown_When_ItIsKilled()
    {
        // The fallback of StopAsync: one SIGKILL on the launcher, with a stand-in that ignores
        // SIGTERM, so nothing in the sandbox would have ended by itself.
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Launcher_Should_TakeTheWholeNamespaceDown_When_ItIsKilled));
        string standIn = run.StandIn("trap '' TERM\ntouch \"$TMPDIR/ready\"\nwhile :; do sleep 1; done");
        await using ClientSandbox sandbox = ClientSandbox.Start(run.Options(standIn), run.StandInToolchain);
        await RealClientEnvironment.EventuallyAsync(() => File.Exists(Path.Combine(sandbox.Plan.Tmp, "ready")), TimeSpan.FromSeconds(30), "the stand-in to ignore SIGTERM");
        IReadOnlyList<ProcessTable.Entry> tree = ProcessTable.Descendants(sandbox.LauncherPid);
        Assert.Contains(tree, e => e.Comm == "Xvfb");
        Assert.Equal("unshare", ProcessTable.Read(sandbox.LauncherPid)!.Value.Comm);

        using (Process launcher = Process.GetProcessById(sandbox.LauncherPid))
        {
            launcher.Kill();
        }

        await RealClientEnvironment.EventuallyAsync(() => !tree.Any(ProcessTable.IsAlive), TimeSpan.FromSeconds(10), "the namespace to die with its launcher");
    }

    [SandboxFact]
    public async Task Sandbox_Should_LeaveNothingRunning_When_TheHostIsKilled()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_LeaveNothingRunning_When_TheHostIsKilled));
        string standIn = run.StandIn("trap 'exit 0' TERM\ntouch \"$TMPDIR/ready\"\nwhile :; do sleep 1; done");

        TimeSpan gone = await HostKillProof.RunAsync(
            run,
            standIn,
            TimeSpan.FromSeconds(40),
            runDirectory => RealClientEnvironment.EventuallyAsync(
                () => File.Exists(Path.Combine(runDirectory, "tmp", "ready")), TimeSpan.FromSeconds(30), "the stand-in to be running"));

        _output.WriteLine($"The stand-in sandbox was gone {gone.TotalMilliseconds:0} ms after its host was killed.");
    }

    [SandboxFact]
    public async Task Sandbox_Should_RefuseToStartTheClient_When_TheDisplayHasNoSocketInThePrivateTmp()
    {
        // A fake Xvfb that reports a display number and serves nothing: the number would send the
        // client to whatever else answers on it, so the inner script must stop before the client.
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_RefuseToStartTheClient_When_TheDisplayHasNoSocketInThePrivateTmp));
        string fakeXvfb = Path.Combine(run.Folder, "fake-xvfb.sh");
        File.WriteAllText(fakeXvfb, "#!/bin/bash\necho 77 >&3\nexec sleep 60\n");
        File.SetUnixFileMode(fakeXvfb, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string standIn = run.StandIn("touch \"$TMPDIR/client-ran\"");

        await using ClientSandbox sandbox = ClientSandbox.Start(
            run.Options(standIn), run.StandInToolchain with { XvfbPath = fakeXvfb });
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(70, sandbox.ExitCode);
        Assert.False(File.Exists(Path.Combine(sandbox.Plan.Tmp, "client-ran")));
        Assert.Contains("display :77 has no socket in the private /tmp", File.ReadAllText(sandbox.Plan.SandboxLog));
    }

    [SandboxFact]
    public async Task Sandbox_Should_RefuseToStartTheClient_When_XvfbReportsNoDisplay()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_RefuseToStartTheClient_When_XvfbReportsNoDisplay));
        string standIn = run.StandIn("touch \"$TMPDIR/client-ran\"");

        await using ClientSandbox sandbox = ClientSandbox.Start(
            run.Options(standIn), run.StandInToolchain with { XvfbPath = "/bin/true" });
        await sandbox.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(70, sandbox.ExitCode);
        Assert.False(File.Exists(Path.Combine(sandbox.Plan.Tmp, "client-ran")));
        Assert.Contains("Xvfb did not report a display number", File.ReadAllText(sandbox.Plan.SandboxLog));
    }

    private static async Task WaitForClientStart(ClientSandbox sandbox)
        => await RealClientEnvironment.EventuallyAsync(
            () => File.Exists(sandbox.Plan.SandboxLog) && File.ReadAllText(sandbox.Plan.SandboxLog).Contains("client start", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30),
            "the client to start",
            () => sandbox.HasExited ? $"the sandbox exited with code {sandbox.ExitCode}" : null);

    private static uint HostUid()
        => uint.Parse(
            File.ReadLines("/proc/self/status").First(l => l.StartsWith("Uid:", StringComparison.Ordinal)).Split('\t', StringSplitOptions.RemoveEmptyEntries)[1],
            CultureInfo.InvariantCulture);
}
