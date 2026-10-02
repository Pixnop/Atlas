using System.Diagnostics;
using System.Xml.Linq;

namespace Atlas.Engine.Tests;

/// <summary>Drives `atlas run --parallel` as a real subprocess tree (the orchestrator plus its
/// worker children, each `dotnet Atlas.Cli.dll run ... --worker --classes ...`) over the
/// Atlas.GuineaPig.Scenarios assembly copied into this project's output. Asserts the stage 2
/// contract end to end: every scenario class is dispatched and finishes, per-test results are
/// aggregated live with their real failure shapes, the summary carries per-class wall clocks and
/// the speedup, per-class isolation summaries cross the worker protocol into the live output,
/// the final summary and the TRX run-level output (issue #66), a failing assembly exits 1, the
/// aggregated TRX file is well-formed, and a worker that outlives its outer timeout is killed
/// and translated into a failed class instead of wedging the queue.</summary>
[Trait("Category", "E2E")]
public class ParallelModeTests
{
    [Fact]
    public void ParallelRun_Should_RunEveryClassAndAggregateOneTrx_When_TwoWorkersDrainTheAssembly()
    {
        string trxPath = Path.Combine(Path.GetTempPath(), $"atlas-parallel-{Guid.NewGuid():N}.trx");
        try
        {
            CliResult result = RunCli("run", TestPaths.GuineaPigDll, "--parallel", "2", "--trx", trxPath);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Running 14 scenario(s) in 7 class(es) on 2 worker(s).", result.StdOut);

            // Every class was dispatched and reported its wall clock, whether it failed before
            // any boot (NotDerived, ConflictingIsolation), crashed a real server mid-class, or
            // passed with real isolation activity (IsolationActivity).
            Assert.Contains("[ConflictingIsolationScenarios] class finished", result.StdOut);
            Assert.Contains("[ControlCharacterScenarios] class finished", result.StdOut);
            Assert.Contains("[DeadHostSequenceScenarios] class finished", result.StdOut);
            Assert.Contains("[HangingScenarios] class finished", result.StdOut);
            Assert.Contains("[IsolationActivityScenarios] class finished", result.StdOut);
            Assert.Contains("[NotDerivedScenarios] class finished", result.StdOut);
            Assert.Contains("[TheoryRowScenarios] class finished", result.StdOut);

            // The aggregated per-test lines carry the same failure shapes the sequential runner
            // would report (nothing lost in the JSONL round trip).
            Assert.Contains("must derive from AtlasScenarioBase", result.StdOut);
            Assert.Contains("ScenarioTimeoutException", result.StdOut);

            // TheoryRowScenarios adds 6 executed results (3 inline rows, 2 runtime-enumerated
            // member rows, 1 no-data failure); rows 1 and 3 plus both member rows pass.
            Assert.Contains("Total: 15, Passed: 7, Failed: 8, Skipped: 0", result.StdOut);
            Assert.Contains("Per-class wall clock:", result.StdOut);
            Assert.Contains("Speedup:", result.StdOut);
            Assert.Contains($"TRX report written to {trxPath}", result.StdOut);

            // The isolation-active class's summary crossed the worker protocol (issue #66): the
            // orchestrator printed it live (verbatim, the exact line plain runs put on stderr)
            // and repeated it in its aggregated final summary, restart cost included.
            Assert.Contains("Isolation summaries:", result.StdOut);
            Assert.Contains(
                "[Atlas] isolation summary for Atlas.GuineaPig.Scenarios.IsolationActivityScenarios:", result.StdOut);
            Assert.Contains("1 capture (", result.StdOut);
            Assert.Contains("1 rollback(s) succeeded (", result.StdOut);
            Assert.Contains("1 restart(s) (", result.StdOut);

            var trx = XDocument.Load(trxPath);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            Assert.Equal(ns + "TestRun", trx.Root!.Name);
            Assert.Equal(15, trx.Root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").Count());
            Assert.Equal(15, trx.Root.Element(ns + "TestDefinitions")!.Elements(ns + "UnitTest").Count());
            XElement summary = trx.Root.Element(ns + "ResultSummary")!;
            Assert.Equal("Failed", summary.Attribute("outcome")!.Value);
            Assert.Equal("15", summary.Element(ns + "Counters")!.Attribute("total")!.Value);
            Assert.Equal("8", summary.Element(ns + "Counters")!.Attribute("failed")!.Value);

            // The summaries also ride the TRX as run-level output (ResultSummary/Output/StdOut,
            // the schema's own slot for run-level messages).
            string trxRunOutput = summary.Element(ns + "Output")!.Element(ns + "StdOut")!.Value;
            Assert.Contains("IsolationActivityScenarios", trxRunOutput);
            Assert.Contains("1 restart(s) (", trxRunOutput);
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
    public void ParallelRun_Should_WriteAValidTrxAndExitWithTheFailureCode_When_AFailureMessageHoldsXmlForbiddenCharacters()
    {
        // XML 1.0 cannot carry a control character, so the TRX writer used to throw after the
        // summary: exit 134 and a truncated report. The report must be written whole (the
        // character kept as a visible escape), nothing may be left beside it, and the exit code
        // must be the run's own: one failed scenario.
        DirectoryInfo directory = Directory.CreateTempSubdirectory("atlas-trx-control-");
        string trxPath = Path.Combine(directory.FullName, "run.trx");
        try
        {
            CliResult result = RunCli(
                "run",
                TestPaths.GuineaPigDll,
                "--parallel",
                "1",
                "--filter",
                "FailWithXmlForbiddenCharacters",
                "--trx",
                trxPath);

            Assert.Equal(1, result.ExitCode);
            Assert.DoesNotContain("Unhandled exception", result.StdErr);
            Assert.Contains("Total: 1, Passed: 0, Failed: 1, Skipped: 0", result.StdOut);
            Assert.Contains($"TRX report written to {trxPath}", result.StdOut);

            var trx = XDocument.Load(trxPath);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            string message = trx.Root!.Element(ns + "Results")!
                .Element(ns + "UnitTestResult")!
                .Element(ns + "Output")!
                .Element(ns + "ErrorInfo")!
                .Element(ns + "Message")!.Value;
            Assert.Contains("payload bytes: \\u0012", message);
            Assert.Contains("noncharacter: \\uFFFE end", message);
            Assert.Equal([trxPath], Directory.GetFileSystemEntries(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SequentialRun_Should_ExitWithTheFailureCode_When_AFailureMessageHoldsXmlForbiddenCharacters()
    {
        // The sequential run writes no XML (--trx is parallel-only), so nothing can crash after
        // its summary; the failing scenario's own exit code is what a CI step must see.
        CliResult result = RunCli("run", TestPaths.GuineaPigDll, "--filter", "FailWithXmlForbiddenCharacters");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("Unhandled exception", result.StdErr);
        Assert.Contains("payload bytes:", result.StdOut);
        Assert.Contains("Total: 1, Passed: 0, Failed: 1, Skipped: 0", result.StdOut);
    }

    [Fact]
    public void ParallelRun_Should_TranslateTheKilledWorkerIntoAFailedClass_When_ClassOutlivesTheWorkerTimeout()
    {
        // The hanging guinea pig cannot finish within 2 s (its server boot alone takes longer),
        // so the outer timeout always fires: the worker tree is killed and crash translation
        // must synthesize the failure instead of losing the class.
        CliResult result = RunCli(
            "run", TestPaths.GuineaPigDll, "--parallel", "1", "--worker-timeout", "2", "--filter", "GameThreadWedges");

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
            WorkingDirectory = TestPaths.OwnOutputDirectory,
        };
        startInfo.ArgumentList.Add(Path.Combine(TestPaths.OwnOutputDirectory, "Atlas.Cli.dll"));
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;

        // Both reads must be started (not awaited) before WaitForExit: a synchronous
        // StandardOutput.ReadToEnd() here used to run first and block until stdout hit EOF, which
        // only happens once the orchestrator process itself exits (ParallelRunner redirects each
        // worker's own stdout and stderr to its own pipe, so a worker does not hold this pipe's
        // write end open). WaitForExit's own 240 s bound was then unreachable code as long as
        // that read never returned, so an orchestrator wedged waiting on a worker it failed to
        // fully kill hung this test forever instead of failing it at the deadline below.
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(240_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "The orchestrator process did not exit within its deadline.");
        return new CliResult(process.ExitCode, stdOutTask.GetAwaiter().GetResult(), stdErrTask.GetAwaiter().GetResult());
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
