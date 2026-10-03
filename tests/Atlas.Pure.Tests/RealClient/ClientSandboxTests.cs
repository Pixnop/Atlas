using System.Diagnostics;
using System.Runtime.Versioning;
using Atlas.Internal.RealClient;

namespace Atlas.Pure.Tests.RealClient;

/// <summary>Contract of the shell around the sandbox plan, for everything that needs no namespace:
/// the run folder layout, the launcher's start information, the user id read from a status file,
/// the refusals that come before anything is created, and the stop ladder, which is driven here
/// with a harmless program (<c>cat</c>, <c>sleep</c>) in the place of the launcher. The engine suite
/// proves the same ladder with the real launcher in the real sandbox.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class ClientSandboxTests : IDisposable
{
    private static readonly string[] DesktopVariables =
        ["DISPLAY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "XAUTHORITY"];

    private static readonly ClientToolchain Toolchain = new(
        "/usr/bin/unshare", "/usr/bin/setpriv", "/usr/bin/Xvfb", "/usr/bin/import", "/opt/vs", "/usr/share/dotnet", "/data/client");

    private readonly string _scratch = Directory.CreateTempSubdirectory("atlas-sandbox-shell").FullName;

    public void Dispose()
    {
        Directory.Delete(_scratch, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UidOf_Should_ReturnTheRealUserId_When_TheStatusFileListsFourIds()
        => Assert.Equal(1000u, ClientSandbox.UidOf(["Name:\tbash", "Umask:\t0022", "Uid:\t1000\t1001\t1002\t1003", "Gid:\t100\t100\t100\t100"]));

    [Fact]
    public void UidOf_Should_Throw_When_ThereIsNoUidLine()
        => Assert.Throws<InvalidOperationException>(() => ClientSandbox.UidOf(["Name:\tbash", "Gid:\t100\t100\t100\t100"]));

    [Fact]
    public void PrepareRunFolder_Should_CreateEveryFolderOfThePlanAndWriteTheInnerScript_When_TheRunIsNew()
    {
        SandboxPlan plan = NewPlan(Path.Combine(_scratch, "run"));

        ClientSandbox.PrepareRunFolder(plan);

        Assert.All(plan.Folders(), folder => Assert.True(Directory.Exists(folder.Path), folder.Path));
        Assert.Equal(SandboxPlan.LoadInnerScript(), File.ReadAllText(plan.InnerScriptFile));
        Assert.Equal(Path.Combine(plan.RunDirectory, "sandbox-inner.sh"), plan.InnerScriptFile);
    }

    [Fact]
    public void PrepareRunFolder_Should_KeepTheRunAndRuntimeFoldersToTheirOwner_When_TheRunIsNew()
    {
        SandboxPlan plan = NewPlan(Path.Combine(_scratch, "run"));
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        ClientSandbox.PrepareRunFolder(plan);

        Assert.Equal(ownerOnly, File.GetUnixFileMode(plan.RunDirectory));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(plan.Xdg));
    }

    [Fact]
    public void PrepareRunFolder_Should_ReplaceAStaleScript_When_TheRunFolderIsReused()
    {
        SandboxPlan plan = NewPlan(Path.Combine(_scratch, "run"));
        Directory.CreateDirectory(plan.RunDirectory);
        File.WriteAllText(plan.InnerScriptFile, "echo stale");

        ClientSandbox.PrepareRunFolder(plan);
        ClientSandbox.PrepareRunFolder(plan);

        Assert.Equal(SandboxPlan.LoadInnerScript(), File.ReadAllText(plan.InnerScriptFile));
    }

    [Fact]
    public void LauncherStartInfo_Should_RunThePlanInTheRunFolderWithTheStdinPipeOfTheGuardian_When_Built()
    {
        SandboxPlan plan = NewPlan("/runs/a");

        ProcessStartInfo psi = ClientSandbox.LauncherStartInfo(plan);

        Assert.Equal(plan.FileName, psi.FileName);
        Assert.Equal(plan.Arguments, psi.ArgumentList);
        Assert.Equal(plan.RunDirectory, psi.WorkingDirectory);
        Assert.True(psi.RedirectStandardInput, "the host's end of the guardian pipe");
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void LauncherStartInfo_Should_GiveTheLauncherThePlansEnvironmentAndNothingOfThisProcess_When_Built()
    {
        SandboxPlan plan = NewPlan("/runs/a");

        ProcessStartInfo psi = ClientSandbox.LauncherStartInfo(plan);

        // This process has a PATH and a HOME of its own (and, on a desktop, a DISPLAY): none of
        // them is in the launcher's environment, only the plan's own list.
        Assert.Equal(
            plan.LauncherEnvironment.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            psi.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)!);
        Assert.Equal("/runs/a/home", psi.Environment["HOME"]);
        Assert.All(DesktopVariables, name => Assert.False(psi.Environment.ContainsKey(name), name));
    }

    [Fact]
    public void Start_Should_RefuseARunFolderTheSandboxWouldHideBeforeCreatingAnything_When_ItIsUnderTmp()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string run = "/tmp/atlas-pure-refused-" + Guid.NewGuid().ToString("N");

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ClientSandbox.Start(new ClientSandboxOptions { RunDirectory = run }, Toolchain));

        Assert.Contains("replaces", error.Message);
        Assert.False(Directory.Exists(run), "nothing is created for a refused run");
    }

    [Fact]
    public async Task StopAsync_Should_LetALauncherThatWatchesItsStdinEndOnItsOwn_When_TheGuardianPipeCloses()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // cat ends at the end of its input, as the guardian's read does: no signal is needed, so the
        // default margin of the ladder (five seconds) is never waited out.
        Process cat = Spawn("cat");
        int pid = cat.Id;
        await using var sandbox = new ClientSandbox(cat, NewPlan("/runs/a"), TimeSpan.FromSeconds(30));
        Assert.Equal(pid, sandbox.LauncherPid);
        Assert.False(sandbox.HasExited);
        Assert.False(sandbox.StopRequested);

        await sandbox.StopAsync();

        Assert.True(sandbox.StopRequested);
        Assert.True(sandbox.HasExited);
        Assert.Equal(0, sandbox.ExitCode);
    }

    [Fact]
    public async Task StopAsync_Should_KillTheLauncherAfterTheGraceAndTheMargin_When_ItIgnoresTheClosedPipe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var sandbox = new ClientSandbox(Spawn("sleep", "60"), NewPlan("/runs/a"), TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        await sandbox.StopAsync();

        Assert.True(sandbox.HasExited);
        Assert.Equal(128 + 9, sandbox.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the ladder is bounded by the grace and the margin");
    }

    [Fact]
    public async Task StopAsync_Should_BeSafeToCallAgain_When_TheSandboxIsAlreadyStopped()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var sandbox = new ClientSandbox(Spawn("cat"), NewPlan("/runs/a"), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await sandbox.StopAsync();

        await sandbox.StopAsync();
        await sandbox.WaitForExitAsync();

        Assert.True(sandbox.StopRequested);
        Assert.Equal(0, sandbox.ExitCode);
    }

    [Fact]
    public async Task StopAsync_Should_OnlyRecordTheRequest_When_TheLauncherHasAlreadyEnded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var sandbox = new ClientSandbox(Spawn("true"), NewPlan("/runs/a"), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await sandbox.WaitForExitAsync();
        Assert.False(sandbox.StopRequested);

        await sandbox.StopAsync();

        Assert.True(sandbox.StopRequested, "a crash detector must still ignore the log lines that follow");
        Assert.Equal(0, sandbox.ExitCode);
    }

    private static Process Spawn(string program, params string[] arguments)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardInput = true };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return Process.Start(psi)!;
    }

    private static SandboxPlan NewPlan(string runDirectory)
        => SandboxPlan.Create(new ClientSandboxOptions { RunDirectory = runDirectory }, Toolchain, 1000, "/usr/bin:/opt/tools/bin");
}
