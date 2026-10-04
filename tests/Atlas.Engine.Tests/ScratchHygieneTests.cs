using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Atlas.Engine.Tests;

/// <summary>Pins what each way of running scenarios leaves under the temp path (issue #182), by
/// running the Atlas.Scratch.Scenarios probe project as a subprocess with a TMPDIR of its own
/// and counting the directories under <c>&lt;TMPDIR&gt;/atlas</c>. A green run must leave none,
/// whichever way it runs: <c>dotnet test</c> (without <c>VSTEST_TESTHOST_SHUTDOWN_TIMEOUT</c>,
/// so vstest kills the test host a hundred milliseconds after the session ends, which only
/// leaves the last class's host released because the class releases it itself, ADR 0011),
/// <c>atlas run</c>, <c>atlas run --parallel</c> and <c>atlas fixture</c>. A red run must keep
/// exactly the failed class's directory, server-main.log included, and nothing else.</summary>
/// <remarks>The TMPDIR is a normalized absolute path: a ".." segment in it breaks
/// <c>Directory.CreateTempSubdirectory</c> in the subprocess. Nothing here lists the shared temp
/// root, so other Atlas processes on the machine cannot disturb a count. The subprocess never
/// inherits the shutdown timeout, which CI sets for the engine suite, nor a run identifier.</remarks>
[Trait("Category", "E2E")]
public class ScratchHygieneTests : IDisposable
{
    // The world seeds and the failing-class variable of the probe project (ProbeScenarios.cs).
    private const string FailVariable = "ATLAS_SCRATCH_PROBE_FAIL";
    private const string RunIdVariable = "ATLAS_RUN_ID";
    private const string ProbeA = "ProbeAScenarios";
    private const string ProbeB = "ProbeBScenarios";
    private const int ProbeASeed = 711;
    private const int ProbeBSeed = 722;

    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(4);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-scratch-hygiene-");

    private static string ProbeDll { get; } =
        typeof(ScratchHygieneTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ScratchProbeDll").Value!;

    private static string CliDll => Path.Combine(TestPaths.OwnOutputDirectory, "Atlas.Cli.dll");

    private string ScratchRoot => Path.Combine(Path.GetFullPath(_root.FullName), "atlas");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DotnetTest_Should_LeaveNoScratch_When_EveryClassPasses()
    {
        // No shutdown timeout: vstest kills the test host a hundred milliseconds after the
        // session ends, and releasing the host takes about a second, so the last class has to
        // be released by its own end (ADR 0011), not by the process exit.
        ProbeRun run = await RunAsync(["test", ProbeDll]);

        Assert.True(run.ExitCode == 0, run.Describe("dotnet test"));
        run.AssertNoScratch();
    }

    [Fact]
    public async Task DotnetTest_Should_KeepOnlyTheFailedClassScratch_When_AClassFails()
    {
        ProbeRun run = await RunAsync(
            ["test", ProbeDll], new Dictionary<string, string> { [FailVariable] = ProbeB, [RunIdVariable] = "hygiene-run-1" });

        Assert.True(run.ExitCode != 0, run.Describe("dotnet test"));
        run.AssertOnlyTheScratchOfSeed(ProbeBSeed, expectedRunId: "hygiene-run-1");
    }

    [Fact]
    public async Task AtlasRun_Should_LeaveNoScratch_When_EveryClassPasses()
    {
        ProbeRun run = await RunAsync([CliDll, "run", ProbeDll]);

        Assert.True(run.ExitCode == 0, run.Describe("atlas run"));
        run.AssertNoScratch();
    }

    [Fact]
    public async Task AtlasRunParallel_Should_LeaveNoScratch_When_EveryClassPasses()
    {
        ProbeRun run = await RunAsync([CliDll, "run", ProbeDll, "--parallel", "2"]);

        Assert.True(run.ExitCode == 0, run.Describe("atlas run --parallel"));
        run.AssertNoScratch();
    }

    [Fact]
    public async Task AtlasRunParallel_Should_KeepOnlyTheFailedClassScratch_When_AClassFails()
    {
        ProbeRun run = await RunAsync(
            [CliDll, "run", ProbeDll, "--parallel", "2"], new Dictionary<string, string> { [FailVariable] = ProbeA });

        Assert.True(run.ExitCode != 0, run.Describe("atlas run --parallel"));
        run.AssertOnlyTheScratchOfSeed(ProbeASeed);
    }

    [Fact]
    public async Task AtlasFixture_Should_WriteTheFixtureAndLeaveNoScratch_When_TheBuilderPasses()
    {
        string fixture = Path.Combine(_root.FullName, "fixtures", "probe.vcdbs");

        ProbeRun run = await RunAsync([CliDll, "fixture", ProbeDll, "--scenario", ProbeA, "--out", fixture]);

        Assert.True(run.ExitCode == 0, run.Describe("atlas fixture"));
        Assert.True(File.Exists(fixture), run.Describe("atlas fixture wrote no file"));
        run.AssertNoScratch();
    }

    /// <summary>Runs <c>dotnet</c> with <paramref name="arguments"/> and a TMPDIR of its own,
    /// without the debugging opt-out that would keep every directory and without the vstest
    /// shutdown timeout.</summary>
    /// <param name="arguments">The dotnet arguments: <c>test</c> or the CLI dll and its command.</param>
    /// <param name="environment">Extra environment variables for the subprocess.</param>
    /// <returns>The run's exit code, output, and the scratch directories it left.</returns>
    private async Task<ProbeRun> RunAsync(string[] arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        Assert.True(File.Exists(ProbeDll), $"the scratch probe is not built at '{ProbeDll}'; build Atlas.Scratch.Scenarios first");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = TestPaths.OwnOutputDirectory,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string tempRoot = Path.GetFullPath(_root.FullName);
        startInfo.Environment["TMPDIR"] = tempRoot;
        startInfo.Environment["TMP"] = tempRoot;
        startInfo.Environment["TEMP"] = tempRoot;
        startInfo.Environment.Remove("ATLAS_KEEP_SCRATCH");
        startInfo.Environment.Remove("VSTEST_TESTHOST_SHUTDOWN_TIMEOUT");
        startInfo.Environment.Remove(RunIdVariable);
        startInfo.Environment.Remove(FailVariable);
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> stdOut = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'dotnet {string.Join(' ', arguments)}' did not finish within {Deadline}.");
        }

        string[] directories = Directory.Exists(ScratchRoot) ? Directory.GetDirectories(ScratchRoot) : [];
        return new ProbeRun(process.ExitCode, await stdOut + await stdErr, directories);
    }

    private sealed record ProbeRun(int ExitCode, string Output, string[] ScratchDirectories)
    {
        public string Describe(string what) =>
            $"{what} exited {ExitCode} and left [{string.Join(", ", ScratchDirectories)}]. Output:\n{Output}";

        public void AssertNoScratch() =>
            Assert.True(ScratchDirectories.Length == 0, Describe("a green run must leave no scratch directory, but"));

        /// <summary>The failed class's directory, and only that: a kept scratch holds the engine's
        /// server-main.log, and the "Using world seed" line in it names the class. It also holds
        /// the witness file of the process that ran the class.</summary>
        /// <param name="seed">The failed probe class's world seed.</param>
        /// <param name="expectedRunId">The run identifier the run was given through the
        /// environment, or <see langword="null"/> for a run that generated its own.</param>
        public void AssertOnlyTheScratchOfSeed(int seed, string? expectedRunId = null)
        {
            Assert.True(ScratchDirectories.Length == 1, Describe("a red run must keep exactly one scratch directory, but"));
            string log = Path.Combine(ScratchDirectories[0], "Logs", "server-main.log");
            Assert.True(File.Exists(log), Describe($"the kept scratch has no '{log}', and"));
            Assert.Contains($"Using world seed: {seed}", File.ReadAllText(log));

            // The witness file names the process that ran the class, which is not this one, and
            // the probe assembly.
            string witnessPath = Path.Combine(ScratchDirectories[0], "atlas-run.json");
            Assert.True(File.Exists(witnessPath), Describe($"the kept scratch has no '{witnessPath}', and"));
            using JsonDocument witness = JsonDocument.Parse(File.ReadAllText(witnessPath));
            Assert.NotEqual(Environment.ProcessId, witness.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal("Atlas.Scratch.Scenarios", witness.RootElement.GetProperty("testAssembly").GetString());
            string? runId = witness.RootElement.GetProperty("runId").GetString();
            if (expectedRunId is null)
            {
                Assert.Matches("^[0-9a-f]{32}$", runId);
            }
            else
            {
                Assert.Equal(expectedRunId, runId);
            }
        }
    }
}
