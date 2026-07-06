using System.Diagnostics;
using System.Text.Json;

namespace Atlas.Engine.Tests;

/// <summary>Drives `atlas run --worker` as a real subprocess (the exact seam the stage 2
/// orchestrator will use) against the Atlas.GuineaPig.Scenarios assembly copied into this
/// project's output, and asserts the JSONL protocol contract: every stdout line parses as JSON,
/// every event carries the version and a type, the class filter runs exactly the chosen class,
/// and a failing class yields a non-zero exit with a well-formed stream ending in run-end.
/// Most tests target the class whose setup guard fails before any host is created (no server
/// boot, fast); the crash-path test boots a real server in the subprocess to prove the engine's
/// console chatter stays off stdout and the stream survives a mid-class crash.</summary>
[Trait("Category", "E2E")]
public class WorkerModeTests
{
    private const string NotDerivedClass = "Atlas.GuineaPig.Scenarios.NotDerivedScenarios";

    private static string OutputDirectory => Path.GetDirectoryName(typeof(WorkerModeTests).Assembly.Location)!;

    [Fact]
    public void WorkerList_Should_EmitDiscoveredEventsAndRunEnd_When_ListingTheAssembly()
    {
        WorkerResult result = RunWorker("run", GuineaPigDll(), "--list", "--worker");

        Assert.Equal(0, result.ExitCode);
        AssertProtocolInvariants(result.Events);
        List<JsonElement> discovered = result.Events.Where(evt => TypeOf(evt) == "discovered").ToList();
        Assert.Equal(5, discovered.Count);
        Assert.Contains(discovered, evt => evt.GetProperty("class").GetString() == NotDerivedClass);
        Assert.All(discovered, evt => Assert.False(string.IsNullOrEmpty(evt.GetProperty("test").GetString())));

        JsonElement runEnd = result.Events[^1];
        Assert.Equal("run-end", TypeOf(runEnd));
        Assert.Equal(5, runEnd.GetProperty("total").GetInt32());
        Assert.Equal(0, runEnd.GetProperty("exitCode").GetInt32());
        Assert.Equal(discovered.Count + 1, result.Events.Count);
    }

    [Fact]
    public void WorkerRun_Should_RunExactlyTheChosenClassAndExitNonZero_When_TheClassFails()
    {
        WorkerResult result = RunWorker("run", GuineaPigDll(), "--worker", "--classes", NotDerivedClass);

        Assert.Equal(1, result.ExitCode);
        AssertProtocolInvariants(result.Events);

        // The stream has exactly the documented shape for a one-class, one-failure run.
        Assert.Equal(
            ["run-start", "class-start", "test-fail", "class-end", "run-end"],
            result.Events.Select(TypeOf).ToList());

        JsonElement runStart = result.Events[0];
        Assert.Equal(NotDerivedClass, runStart.GetProperty("classes")[0].GetString());
        Assert.True(runStart.GetProperty("pid").GetInt32() > 0);
        Assert.EndsWith("Atlas.GuineaPig.Scenarios.dll", runStart.GetProperty("assembly").GetString());

        // Exact class filter: nothing from the other guinea pig classes leaks into the run.
        Assert.DoesNotContain("HangingScenarios", result.StdOut);
        Assert.DoesNotContain("DeadHostSequenceScenarios", result.StdOut);

        JsonElement fail = result.Events[2];
        Assert.Equal(NotDerivedClass, fail.GetProperty("class").GetString());
        Assert.Contains("AtlasSetupException", fail.GetProperty("message").GetString());
        Assert.Contains("must derive from AtlasScenarioBase", fail.GetProperty("message").GetString());

        JsonElement runEnd = result.Events[^1];
        Assert.Equal(1, runEnd.GetProperty("total").GetInt32());
        Assert.Equal(1, runEnd.GetProperty("failed").GetInt32());
        Assert.Equal(1, runEnd.GetProperty("exitCode").GetInt32());
        Assert.True(runEnd.GetProperty("wallClockMs").GetInt64() >= 0);
    }

    [Fact]
    public void WorkerRun_Should_KeepStdoutPureAndCloseTheStream_When_TheServerCrashesMidClass()
    {
        // Boots a real embedded server in the worker subprocess; scenario A kills its game
        // thread, scenario B fail-fasts on the dead host. The engine's console chatter must all
        // land on stderr, and the stream must still end with a well-formed run-end.
        WorkerResult result = RunWorker(
            "run", GuineaPigDll(), "--worker", "--classes", "Atlas.GuineaPig.Scenarios.DeadHostSequenceScenarios");

        Assert.Equal(1, result.ExitCode);
        AssertProtocolInvariants(result.Events);
        Assert.Equal(
            ["run-start", "class-start", "test-fail", "test-fail", "class-end", "run-end"],
            result.Events.Select(TypeOf).ToList());
        Assert.Contains("Embedded server died", result.Events[2].GetProperty("message").GetString());
        Assert.Contains("ServerCrashedException", result.Events[3].GetProperty("message").GetString());
        Assert.Equal(2, result.Events[4].GetProperty("failed").GetInt32());
        Assert.Equal(1, result.Events[^1].GetProperty("exitCode").GetInt32());

        // The server booted (its chatter proves it) and stayed off stdout.
        Assert.Contains("Server logger started", result.StdErr);
    }

    [Fact]
    public void WorkerRun_Should_ExitOneWithEmptyTotals_When_NoClassMatches()
    {
        WorkerResult result = RunWorker("run", GuineaPigDll(), "--worker", "--classes", "No.Such.Class");

        Assert.Equal(1, result.ExitCode);
        AssertProtocolInvariants(result.Events);
        JsonElement runEnd = result.Events[^1];
        Assert.Equal("run-end", TypeOf(runEnd));
        Assert.Equal(0, runEnd.GetProperty("total").GetInt32());
        Assert.Equal(1, runEnd.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void WorkerRun_Should_ReportTheReasonOnTheStreamAndExitTwo_When_EnvironmentIsMissing()
    {
        WorkerResult result = RunWorker(
            ["run", GuineaPigDll(), "--worker", "--classes", NotDerivedClass],
            environment => environment.Remove("VINTAGE_STORY"));

        Assert.Equal(2, result.ExitCode);
        AssertProtocolInvariants(result.Events);
        Assert.Equal(["run-start", "error", "run-end"], result.Events.Select(TypeOf).ToList());
        Assert.Contains("VINTAGE_STORY", result.Events[1].GetProperty("message").GetString());
        Assert.Equal(2, result.Events[^1].GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void Run_Should_RejectClasses_When_WorkerFlagIsAbsent()
    {
        WorkerResult result = RunWorker("run", GuineaPigDll(), "--classes", NotDerivedClass);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Events);
        Assert.Contains("--classes requires --worker", result.StdErr);
    }

    private static string GuineaPigDll() => Path.Combine(OutputDirectory, "Atlas.GuineaPig.Scenarios.dll");

    private static string TypeOf(JsonElement evt) => evt.GetProperty("type").GetString()!;

    private static void AssertProtocolInvariants(IReadOnlyList<JsonElement> events)
    {
        Assert.All(events, evt =>
        {
            Assert.Equal(1, evt.GetProperty("v").GetInt32());
            Assert.False(string.IsNullOrEmpty(evt.GetProperty("type").GetString()));
        });
    }

    private static WorkerResult RunWorker(params string[] args) => RunWorker(args, _ => { });

    private static WorkerResult RunWorker(string[] args, Action<IDictionary<string, string?>> configureEnvironment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = OutputDirectory,
        };
        startInfo.ArgumentList.Add(Path.Combine(OutputDirectory, "Atlas.Cli.dll"));
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        configureEnvironment(startInfo.Environment);

        using var process = Process.Start(startInfo)!;
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        string stdOut = process.StandardOutput.ReadToEnd();
        bool exited = process.WaitForExit(120_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "The worker process did not exit within its deadline.");

        // Every stdout line must parse as JSON: worker mode allows no human chatter on stdout.
        List<JsonElement> events = stdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
        return new WorkerResult(process.ExitCode, events, stdOut, stdErrTask.GetAwaiter().GetResult());
    }

    private sealed record WorkerResult(int ExitCode, IReadOnlyList<JsonElement> Events, string StdOut, string StdErr);
}
