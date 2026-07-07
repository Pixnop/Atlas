using System.Diagnostics;
using System.Xml.Linq;

namespace Atlas.Engine.Tests;

/// <summary>Drives `atlas run --parallel` as a real subprocess tree (the orchestrator plus its
/// worker children, each `dotnet Atlas.Cli.dll run ... --worker --classes ...`) over the
/// Atlas.GuineaPig.Scenarios assembly copied into this project's output. Asserts the stage 2
/// contract end to end: every scenario class is dispatched and finishes, per-test results are
/// aggregated live with their real failure shapes, the summary carries per-class wall clocks and
/// the speedup, a failing assembly exits 1, the aggregated TRX file is well-formed, and a worker
/// that outlives its outer timeout is killed and translated into a failed class instead of
/// wedging the queue.</summary>
[Trait("Category", "E2E")]
public class ParallelModeTests
{
    private static string OutputDirectory => Path.GetDirectoryName(typeof(ParallelModeTests).Assembly.Location)!;

    private static string GuineaPigDll => Path.Combine(OutputDirectory, "Atlas.GuineaPig.Scenarios.dll");

    [Fact]
    public void ParallelRun_Should_RunEveryClassAndAggregateOneTrx_When_TwoWorkersDrainTheAssembly()
    {
        string trxPath = Path.Combine(Path.GetTempPath(), $"atlas-parallel-{Guid.NewGuid():N}.trx");
        try
        {
            CliResult result = RunCli("run", GuineaPigDll, "--parallel", "2", "--trx", trxPath);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Running 5 scenario(s) in 4 class(es) on 2 worker(s).", result.StdOut);

            // Every class was dispatched and reported its wall clock, whether it failed before
            // any boot (NotDerived, ConflictingIsolation) or crashed a real server mid-class.
            Assert.Contains("[ConflictingIsolationScenarios] class finished", result.StdOut);
            Assert.Contains("[DeadHostSequenceScenarios] class finished", result.StdOut);
            Assert.Contains("[HangingScenarios] class finished", result.StdOut);
            Assert.Contains("[NotDerivedScenarios] class finished", result.StdOut);

            // The aggregated per-test lines carry the same failure shapes the sequential runner
            // would report (nothing lost in the JSONL round trip).
            Assert.Contains("must derive from AtlasScenarioBase", result.StdOut);
            Assert.Contains("ScenarioTimeoutException", result.StdOut);
            Assert.Contains("Total: 5, Passed: 0, Failed: 5, Skipped: 0", result.StdOut);
            Assert.Contains("Per-class wall clock:", result.StdOut);
            Assert.Contains("Speedup:", result.StdOut);
            Assert.Contains($"TRX report written to {trxPath}", result.StdOut);

            XDocument trx = XDocument.Load(trxPath);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            Assert.Equal(ns + "TestRun", trx.Root!.Name);
            Assert.Equal(5, trx.Root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").Count());
            Assert.Equal(5, trx.Root.Element(ns + "TestDefinitions")!.Elements(ns + "UnitTest").Count());
            XElement summary = trx.Root.Element(ns + "ResultSummary")!;
            Assert.Equal("Failed", summary.Attribute("outcome")!.Value);
            Assert.Equal("5", summary.Element(ns + "Counters")!.Attribute("total")!.Value);
            Assert.Equal("5", summary.Element(ns + "Counters")!.Attribute("failed")!.Value);
        }
        finally
        {
            if (File.Exists(trxPath))
            {
                File.Delete(trxPath);
            }
        }
    }

    [Fact]
    public void ParallelRun_Should_TranslateTheKilledWorkerIntoAFailedClass_When_ClassOutlivesTheWorkerTimeout()
    {
        // The hanging guinea pig cannot finish within 2 s (its server boot alone takes longer),
        // so the outer timeout always fires: the worker tree is killed and crash translation
        // must synthesize the failure instead of losing the class.
        CliResult result = RunCli(
            "run", GuineaPigDll, "--parallel", "1", "--worker-timeout", "2", "--filter", "GameThreadWedges");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("worker timed out", result.StdOut);
        Assert.Contains("exceeded its 2 s timeout", result.StdOut);
        Assert.Contains("[HangingScenarios] class finished", result.StdOut);
        Assert.Contains("Total: 1, Passed: 0, Failed: 1, Skipped: 0", result.StdOut);
    }

    private static CliResult RunCli(params string[] args)
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

        using var process = Process.Start(startInfo)!;
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        string stdOut = process.StandardOutput.ReadToEnd();
        bool exited = process.WaitForExit(240_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "The orchestrator process did not exit within its deadline.");
        return new CliResult(process.ExitCode, stdOut, stdErrTask.GetAwaiter().GetResult());
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
