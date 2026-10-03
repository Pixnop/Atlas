using Atlas.Internal.RealClient;

namespace Atlas.Pure.Tests.RealClient;

/// <summary>Contract of the sandbox launch plan: which namespaces, which variables, which paths.
/// These are the guarantees that live in the arguments of the launch, so they are checked without
/// starting anything; the engine suite proves the running sandbox.</summary>
public class SandboxPlanTests
{
    private static readonly string[] DesktopVariables =
        ["DISPLAY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "XAUTHORITY"];

    private static readonly ClientToolchain Toolchain = new(
        "/usr/bin/unshare", "/usr/bin/Xvfb", "/usr/bin/import", "/opt/vs", "/usr/share/dotnet", "/data/client");

    [Fact]
    public void NamespaceFlags_Should_MakeUserMountAndPidNamespacesTiedToTheLauncher_When_TheNetworkIsShared()
    {
        IReadOnlyList<string> flags = SandboxPlan.NamespaceFlags(isolateNetwork: false);

        Assert.Equal(["--user", "--map-root-user", "--mount", "--pid", "--fork", "--kill-child", "--mount-proc"], flags);
    }

    [Fact]
    public void NamespaceFlags_Should_AddANetworkNamespace_When_Asked()
        => Assert.Equal("--net", SandboxPlan.NamespaceFlags(isolateNetwork: true)[^1]);

    [Fact]
    public void BuildClientEnvironment_Should_ListExactlyTheWhitelist_When_Built()
    {
        IReadOnlyDictionary<string, string> environment = SandboxPlan.BuildClientEnvironment("/runs/a", Toolchain);

        Assert.Equal(
            ["ALSOFT_DRIVERS", "DOTNET_ROOT", "FONTCONFIG_FILE", "HOME", "LANG", "LIBGL_ALWAYS_SOFTWARE", "OPENTK_4_USE_WAYLAND", "PATH", "TMPDIR", "XDG_RUNTIME_DIR"],
            environment.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BuildClientEnvironment_Should_KeepTheDesktopOut_When_Built()
    {
        IReadOnlyDictionary<string, string> environment = SandboxPlan.BuildClientEnvironment("/runs/a", Toolchain);

        // DISPLAY is added by the inner script, from the private Xvfb, and by nothing else.
        Assert.All(DesktopVariables, name => Assert.DoesNotContain(name, environment.Keys));
    }

    [Fact]
    public void BuildClientEnvironment_Should_PointHomeAndTheTemporaryFoldersIntoTheRunFolder_When_Built()
    {
        IReadOnlyDictionary<string, string> environment = SandboxPlan.BuildClientEnvironment("/runs/a", Toolchain);

        Assert.Equal("/runs/a/home", environment["HOME"]);
        Assert.Equal("/runs/a/tmp", environment["TMPDIR"]);
        Assert.Equal("/runs/a/xdg", environment["XDG_RUNTIME_DIR"]);
    }

    [Fact]
    public void BuildClientEnvironment_Should_SetTheRenderingAndRuntimeValues_When_Built()
    {
        IReadOnlyDictionary<string, string> environment = SandboxPlan.BuildClientEnvironment("/runs/a", Toolchain);

        Assert.Equal("1", environment["LIBGL_ALWAYS_SOFTWARE"]);
        Assert.Equal("null", environment["ALSOFT_DRIVERS"]);
        Assert.Equal("0", environment["OPENTK_4_USE_WAYLAND"]);
        Assert.Equal("/opt/vs/fonts.conf", environment["FONTCONFIG_FILE"]);
        Assert.Equal("/usr/share/dotnet", environment["DOTNET_ROOT"]);
        Assert.Equal("C.UTF-8", environment["LANG"]);
        Assert.Equal(SandboxPlan.ClientPath, environment["PATH"]);
    }

    [Fact]
    public void Create_Should_GiveTheLauncherOnlyPathAndTheSandboxSettings_When_Built()
    {
        SandboxPlan plan = Create(new ClientSandboxOptions { RunDirectory = "/runs/a" });

        Assert.Equal(
            ["ATLAS_SB_IMPORT", "ATLAS_SB_INSTALL", "ATLAS_SB_ISOLATE_NET", "ATLAS_SB_KILL_AFTER", "ATLAS_SB_PROGRAM", "ATLAS_SB_RUN", "ATLAS_SB_SHOTS", "ATLAS_SB_TIMEOUT", "ATLAS_SB_UID", "ATLAS_SB_XVFB", "PATH"],
            plan.LauncherEnvironment.Keys.Order(StringComparer.Ordinal));
        Assert.All(DesktopVariables, name => Assert.DoesNotContain(name, plan.LauncherEnvironment.Keys));
        Assert.Equal("/usr/bin:/opt/tools/bin", plan.LauncherEnvironment["PATH"]);
    }

    [Fact]
    public void Create_Should_FallBackToTheFixedPath_When_TheHostHasNone()
        => Assert.Equal(
            SandboxPlan.ClientPath,
            SandboxPlan.Create(new ClientSandboxOptions { RunDirectory = "/runs/a" }, Toolchain, 1000, null, "script").LauncherEnvironment["PATH"]);

    [Fact]
    public void Create_Should_StartUnshareThroughAShellThatLogsItsOutput_When_Built()
    {
        SandboxPlan plan = Create(new ClientSandboxOptions { RunDirectory = "/runs/a" });

        Assert.Equal("/bin/sh", plan.FileName);
        Assert.Equal(["-c", "exec \"$@\" >\"$0\" 2>&1", "/runs/a/launcher.log", "/usr/bin/unshare"], plan.Arguments.Take(4));
        Assert.Equal(
            SandboxPlan.NamespaceFlags(isolateNetwork: false),
            plan.Arguments.Skip(4).Take(SandboxPlan.NamespaceFlags(false).Count));
    }

    [Fact]
    public void Create_Should_PassTheScriptAsAnArgumentAndThenTheEnvironmentThenTheClientArguments_When_Built()
    {
        SandboxPlan plan = Create(new ClientSandboxOptions
        {
            RunDirectory = "/runs/a",
            Arguments = ["--connect", "127.0.0.1:4242", "--pw", "s e c r e t"],
        });

        int bash = plan.Arguments.ToList().IndexOf("bash");
        Assert.Equal(["bash", "-c", "the script", "atlas-sandbox"], plan.Arguments.Skip(bash).Take(4));
        int separator = plan.Arguments.ToList().IndexOf("--");
        string[] pairs = [.. plan.Arguments.Skip(bash + 4).Take(separator - bash - 4)];
        Assert.Equal(plan.ClientEnvironment.Select(p => $"{p.Key}={p.Value}"), pairs);
        Assert.Equal(
            ["--dataPath", "/data/client", "--logPath", "/runs/a/logs", "--connect", "127.0.0.1:4242", "--pw", "s e c r e t"],
            plan.Arguments.Skip(separator + 1));
    }

    [Fact]
    public void Create_Should_RunTheInstallsClient_When_NoProgramIsOverridden()
        => Assert.Equal(
            "/opt/vs/Vintagestory",
            Create(new ClientSandboxOptions { RunDirectory = "/runs/a" }).LauncherEnvironment["ATLAS_SB_PROGRAM"]);

    [Fact]
    public void Create_Should_RunTheOverride_When_ATestSeamNamesAProgram()
        => Assert.Equal(
            "/stand/in",
            Create(new ClientSandboxOptions { RunDirectory = "/runs/a", ProgramOverride = "/stand/in" }).LauncherEnvironment["ATLAS_SB_PROGRAM"]);

    [Fact]
    public void Create_Should_RoundBoundsUpToWholeSeconds_When_TheyAreFractions()
    {
        SandboxPlan plan = Create(new ClientSandboxOptions
        {
            RunDirectory = "/runs/a",
            Timeout = TimeSpan.FromSeconds(90.2),
            StopGrace = TimeSpan.FromSeconds(2.5),
            ScreenshotInterval = TimeSpan.FromSeconds(0.1),
        });

        Assert.Equal("91", plan.LauncherEnvironment["ATLAS_SB_TIMEOUT"]);
        Assert.Equal("3", plan.LauncherEnvironment["ATLAS_SB_KILL_AFTER"]);
        Assert.Equal("1", plan.LauncherEnvironment["ATLAS_SB_SHOTS"]);
    }

    [Fact]
    public void Create_Should_TurnScreenshotsOff_When_ImportIsNotInstalled()
    {
        SandboxPlan plan = SandboxPlan.Create(
            new ClientSandboxOptions { RunDirectory = "/runs/a" },
            Toolchain with { ImportPath = null },
            1000,
            "/usr/bin",
            "the script");

        Assert.Equal("0", plan.LauncherEnvironment["ATLAS_SB_SHOTS"]);
        Assert.Equal(string.Empty, plan.LauncherEnvironment["ATLAS_SB_IMPORT"]);
    }

    [Fact]
    public void Create_Should_AskForANetworkNamespaceOnlyWhenTheOptionSaysSo()
    {
        SandboxPlan shared = Create(new ClientSandboxOptions { RunDirectory = "/runs/a" });
        SandboxPlan isolated = Create(new ClientSandboxOptions { RunDirectory = "/runs/a", IsolateNetwork = true });

        Assert.DoesNotContain("--net", shared.Arguments);
        Assert.Equal("0", shared.LauncherEnvironment["ATLAS_SB_ISOLATE_NET"]);
        Assert.Contains("--net", isolated.Arguments);
        Assert.Equal("1", isolated.LauncherEnvironment["ATLAS_SB_ISOLATE_NET"]);
    }

    [Fact]
    public void Folders_Should_ListTheRunLayoutAndKeepTheRunFolderAndRuntimeDirOwnerOnly_When_Built()
    {
        SandboxPlan plan = Create(new ClientSandboxOptions { RunDirectory = "/runs/a" });

        Assert.Equal(
            [("/runs/a", true), ("/runs/a/logs", false), ("/runs/a/shots", false), ("/runs/a/home", false), ("/runs/a/tmp", false), ("/runs/a/xdg", true)],
            plan.Folders());
        Assert.Equal("/runs/a/sandbox.log", plan.SandboxLog);
        Assert.Equal("/runs/a/client.stdout", plan.ClientStdout);
    }

    [Fact]
    public void LoadInnerScript_Should_ReturnTheEmbeddedScript_When_Read()
    {
        string script = SandboxPlan.LoadInnerScript();

        Assert.StartsWith("#!/usr/bin/env bash", script);
    }

    [Theory]
    [InlineData("ulimit -c 0")]
    [InlineData("mount -t tmpfs tmpfs /tmp")]
    [InlineData("-displayfd")]
    [InlineData("env -i ")]
    [InlineData("timeout --signal=TERM --kill-after=")]
    [InlineData("read -r _ <&4")]
    public void LoadInnerScript_Should_KeepTheLineThatCarriesEachGuarantee_When_Read(string line)
        => Assert.Contains(line, SandboxPlan.LoadInnerScript());

    [Fact]
    public void LoadInnerScript_Should_RefuseBeforeStartingTheClient_When_APrivateMountOrTheDisplayFails()
    {
        string script = SandboxPlan.LoadInnerScript();

        int client = script.IndexOf("note \"client start\"", StringComparison.Ordinal);
        Assert.True(client > 0);
        foreach (string refusal in new[] { "cannot mount a private /tmp", "/tmp is not the private tmpfs", "Xvfb did not report a display number", "has no socket in the private /tmp" })
        {
            int at = script.IndexOf(refusal, StringComparison.Ordinal);
            Assert.True(at > 0 && at < client, $"'{refusal}' must be checked before the client starts");
        }
    }

    [Theory]
    [InlineData("/tmp", "/tmp")]
    [InlineData("/tmp/atlas/run", "/tmp")]
    [InlineData("/run/user/1000/x", "/run/user/1000")]
    [InlineData("/home/dev/../../tmp/x", "/tmp")]
    public void HiddenBySandbox_Should_NameTheReplacedFolder_When_ThePathIsUnderIt(string path, string hider)
        => Assert.Equal(hider, ClientSandboxOptions.HiddenBySandbox(path, 1000));

    [Theory]
    [InlineData("/tmpfs/x")]
    [InlineData("/home/dev/tmp")]
    [InlineData("/run/user/1001/x")]
    [InlineData("/run/user/10000")]
    public void HiddenBySandbox_Should_ReturnNull_When_ThePathIsOnlyNamedLikeAReplacedFolder(string path)
        => Assert.Null(ClientSandboxOptions.HiddenBySandbox(path, 1000));

    [Theory]
    [InlineData("/tmp/atlas/run", "/data/client", "run directory")]
    [InlineData("/runs/a", "/tmp/client", "data path")]
    [InlineData("/runs/a", "/run/user/1000/client", "data path")]
    public void Validate_Should_RefuseAPathTheSandboxWouldHide_When_Asked(string run, string data, string what)
    {
        var options = new ClientSandboxOptions { RunDirectory = run };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => options.Validate(Toolchain with { DataPath = data }, 1000));

        Assert.Contains(what, error.Message);
        Assert.Contains("replaces", error.Message);
    }

    [Fact]
    public void Validate_Should_RefuseARunDirectoryThatIsNotEmpty_When_ItExists()
    {
        // The test's own output folder exists and is full.
        var options = new ClientSandboxOptions { RunDirectory = AppContext.BaseDirectory };

        ArgumentException error = Assert.Throws<ArgumentException>(() => options.Validate(Toolchain, 1000));

        Assert.Contains("never reused", error.Message);
    }

    [Fact]
    public void Validate_Should_AcceptARunDirectoryThatDoesNotExistYet_When_ThePathsAreClear()
    {
        var options = new ClientSandboxOptions { RunDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-run") };

        options.Validate(Toolchain, 1000);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, -1)]
    public void Validate_Should_RefuseBoundsThatMakeNoSense_When_Asked(double timeout, double grace, double shots)
    {
        var options = new ClientSandboxOptions
        {
            RunDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-run"),
            Timeout = TimeSpan.FromSeconds(timeout),
            StopGrace = TimeSpan.FromSeconds(grace),
            ScreenshotInterval = TimeSpan.FromSeconds(shots),
        };

        Assert.Throws<ArgumentException>(() => options.Validate(Toolchain, 1000));
    }

    private static SandboxPlan Create(ClientSandboxOptions options)
        => SandboxPlan.Create(options, Toolchain, 1000, "/usr/bin:/opt/tools/bin", "the script");
}
