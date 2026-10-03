using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Atlas.Internal.RealClient;
using Xunit.Abstractions;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>The stock game client, started for real in the sandbox on a fresh data path that has
/// never held a login, so the login screen is the expected end state. Each test needs a full
/// client install (<c>VINTAGE_STORY</c>) and a machine that can run the sandbox, or is skipped
/// with the reason; they carry the <c>AtlasClient</c> trait, not <c>E2E</c>, so no CI shard
/// selects them. They never touch a game data path that someone logged in on: the only data path
/// is the new folder <see cref="RealClientEnvironment.NewRun"/> hands out. One client at a time
/// (the assembly does not run tests in parallel), and none while a game is running on the same
/// machine, which is the person's to check.</summary>
[Trait("Category", "AtlasClient")]
[UnsupportedOSPlatform("windows")]
public class RealClientLoginScreenTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly ITestOutputHelper _output;

    public RealClientLoginScreenTests(ITestOutputHelper output) => _output = output;

    [RealClientFact]
    public async Task Client_Should_ReachItsLoginScreenInTheSandboxAndLeaveNothingBehind_When_TheDataPathIsFresh()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Client_Should_ReachItsLoginScreenInTheSandboxAndLeaveNothingBehind_When_TheDataPathIsFresh));
        ClientToolchain toolchain = RealClientEnvironment.Availability.Toolchain! with { DataPath = run.DataPath };
        Assert.False(Directory.Exists(run.DataPath), "the data path must be new");
        string[] hostSockets = RealClientEnvironment.HostX11Sockets();
        DateTime start = DateTime.Now;
        ClientSandboxOptions options = run.Options() with
        {
            Timeout = TimeSpan.FromMinutes(3),
            ScreenshotInterval = TimeSpan.FromSeconds(5),
        };

        IReadOnlyList<ProcessTable.Entry> everything;
        ClientSandbox sandbox = ClientSandbox.Start(options, toolchain);
        try
        {
            string mainLog = Path.Combine(sandbox.Plan.Logs, "client-main.log");
            await RealClientEnvironment.EventuallyAsync(
                () => File.Exists(mainLog) && File.ReadAllText(mainLog).Contains(RealClientEnvironment.LoginScreenMarker, StringComparison.Ordinal),
                TimeSpan.FromSeconds(120),
                $"'{RealClientEnvironment.LoginScreenMarker}' in client-main.log",
                () => sandbox.HasExited ? $"the sandbox exited with code {sandbox.ExitCode} (see {sandbox.Plan.RunDirectory})" : null);

            // While the client is on its login screen: what it was given and what it can reach.
            everything = ProcessTable.Descendants(sandbox.LauncherPid);
            ProcessTable.Entry client = Assert.Single(everything, e => e.Comm == "Vintagestory");
            Assert.Contains(everything, e => e.Comm == "Xvfb");
            AssertClientEnvironment(client.Pid, sandbox.Plan);
            Assert.Contains(
                File.ReadLines($"/proc/{client.Pid}/limits"),
                line => line.StartsWith("Max core file size", StringComparison.Ordinal)
                        && line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^3] == "0"
                        && line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^2] == "0");
            Assert.Equal(hostSockets, RealClientEnvironment.HostX11Sockets());
            if (toolchain.ImportPath is not null)
            {
                await RealClientEnvironment.EventuallyAsync(
                    () => Directory.EnumerateFiles(sandbox.Plan.Screenshots, "*.png").Any(),
                    TimeSpan.FromSeconds(30),
                    "a screenshot of the private display");
            }

            await sandbox.StopAsync();

            Assert.True(sandbox.HasExited);
            Assert.True(sandbox.StopRequested);
            Assert.Contains("guardian: the host closed the pipe", File.ReadAllText(sandbox.Plan.SandboxLog));
        }
        finally
        {
            await sandbox.DisposeAsync();
        }

        Assert.DoesNotContain(everything, ProcessTable.IsAlive);
        Assert.Equal(hostSockets, RealClientEnvironment.HostX11Sockets());
        Assert.Empty(Directory.EnumerateFiles(run.Folder, "core*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(toolchain.InstallDirectory, "core*"));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(run.RunDirectory));
        Assert.Empty(CoredumpFilesOfTheClientSince(start));
    }

    [RealClientFact]
    public async Task Sandbox_Should_LeaveNothingRunning_When_TheHostOfARealClientIsKilled()
    {
        RealClientEnvironment.TestRun run = RealClientEnvironment.NewRun(nameof(Sandbox_Should_LeaveNothingRunning_When_TheHostOfARealClientIsKilled));
        Assert.False(Directory.Exists(run.DataPath), "the data path must be new");

        TimeSpan gone = await HostKillProof.RunAsync(
            run,
            program: null,
            mustSurviveNoLongerThan: TimeSpan.FromSeconds(40),
            waitUntilSettled: runDirectory => RealClientEnvironment.EventuallyAsync(
                () => File.Exists(Path.Combine(runDirectory, "logs", "client-main.log"))
                      && File.ReadAllText(Path.Combine(runDirectory, "logs", "client-main.log")).Contains(RealClientEnvironment.LoginScreenMarker, StringComparison.Ordinal),
                TimeSpan.FromSeconds(120),
                $"'{RealClientEnvironment.LoginScreenMarker}' in client-main.log"));

        _output.WriteLine($"The real client's sandbox was gone {gone.TotalMilliseconds:0} ms after its host was killed.");
        Assert.Empty(Directory.EnumerateFiles(run.Folder, "core*", SearchOption.AllDirectories));
    }

    // The initial environment of the real client process: the plan's whitelist and the private
    // display, and nothing of the desktop.
    private static void AssertClientEnvironment(int pid, SandboxPlan plan)
    {
        Dictionary<string, string> environment = File.ReadAllText($"/proc/{pid}/environ")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        string display = Assert.Contains("DISPLAY", environment);
        Assert.Equal(
            ":" + File.ReadAllText(Path.Combine(plan.RunDirectory, "display")).Trim(),
            display);
        Assert.NotEqual(Environment.GetEnvironmentVariable("DISPLAY"), display);
        environment.Remove("DISPLAY");
        Assert.Equal(
            plan.ClientEnvironment.OrderBy(p => p.Key, StringComparer.Ordinal),
            environment.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    // Core files the system recorded for the client since a moment: rows of coredumpctl that
    // name the client and say a file is present. A row with no file is what a dump refused by
    // RLIMIT_CORE 0 looks like, and is fine. Without coredumpctl there is nothing to read.
    private static string[] CoredumpFilesOfTheClientSince(DateTime since)
    {
        var psi = new ProcessStartInfo("coredumpctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "list", "--no-pager", "--since", since.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) })
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return
            [
                .. output.Split('\n')
                    .Where(line => line.Contains("Vintagestory", StringComparison.Ordinal)
                                   && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("present")),
            ];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }
}
