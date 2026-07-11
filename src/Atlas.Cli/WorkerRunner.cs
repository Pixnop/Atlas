using System.Diagnostics;
using System.Reflection;
using Xunit.Runners;

namespace Atlas.Cli;

/// <summary>Implements `atlas run --worker`: the same in-process sequential execution as
/// <see cref="ScenarioRunner"/> (one process, one live server at a time), but restricted to an
/// optional set of scenario classes (xunit's <c>TypesToRun</c>, exact full-name match) and
/// reporting exclusively as JSONL protocol events on stdout: the seam the stage 2 orchestrator
/// consumes. Deliberately thin: every protocol decision lives in <see cref="WorkerRunSession"/>.</summary>
internal static class WorkerRunner
{
    /// <summary>Runs the selected scenario classes and streams protocol events to
    /// <paramref name="output"/>. The stream always ends with a run-end line, even when the run
    /// crashes: a fail-safe finally (plus an unhandled-exception hook for non-runner threads)
    /// closes it, so the orchestrator never sees a stream ending silently.</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter deciding which scenarios run.</param>
    /// <param name="classes">Fully qualified class names to run; null runs the whole assembly.</param>
    /// <param name="environmentError">A VINTAGE_STORY validation error, if any: reported as an
    /// error event followed by run-end with exit code 2, so the orchestrator learns the reason
    /// from the stream itself.</param>
    /// <param name="output">Destination for the JSONL event stream (the worker's stdout).</param>
    /// <returns>The process exit code (0 ok, 1 failures or empty run, 2 environment error).</returns>
    public static int Run(
        string assemblyPath, ScenarioFilter filter, IReadOnlyList<string>? classes, string? environmentError, TextWriter output)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        var session = new WorkerRunSession(fullPath, classes, Environment.ProcessId, AtlasVersion());
        var writer = new WorkerEventWriter(output);
        var stopwatch = Stopwatch.StartNew();
        writer.Write(session.Start());

        if (environmentError is not null)
        {
            writer.WriteAll(session.RecordError("EnvironmentError", environmentError));
            writer.WriteAll(session.Complete(stopwatch.ElapsedMilliseconds, exitCode: 2));
            return 2;
        }

        // Last line of defense: an unhandled exception on a non-runner thread (the embedded
        // server's own threads, say) kills the process after this handler runs, and the stream
        // must still end with test-fail/run-end lines rather than silently.
        UnhandledExceptionEventHandler crashHandler = (_, e) =>
        {
            writer.WriteAll(session.RecordCrash(e.ExceptionObject.ToString() ?? "unhandled exception"));
            writer.WriteAll(session.Complete(stopwatch.ElapsedMilliseconds));
        };
        AppDomain.CurrentDomain.UnhandledException += crashHandler;

        try
        {
            using var resolver = new ScenarioAssemblyResolver(Path.GetDirectoryName(fullPath)!);
            using var done = new ManualResetEventSlim();

            // Per-class isolation summaries print to stderr at every host hand-off; protocol
            // consumers need them on stdout, so the harness sink (installed by name, like the
            // fixture-harvest seam) turns each one into a class-summary event.
            using IDisposable summarySink = IsolationSummaryHook.Register(
                (className, summary) => writer.WriteAll(session.RecordClassSummary(className, summary)));

            var runner = AssemblyRunner.WithoutAppDomain(fullPath);
            try
            {
                runner.TestCaseFilter = testCase => filter.Matches(testCase.DisplayName);
                runner.OnTestPassed = info => writer.WriteAll(
                    session.RecordPass(info.TypeName, info.TestDisplayName, info.ExecutionTime));
                runner.OnTestFailed = info => writer.WriteAll(session.RecordFail(
                    info.TypeName, info.TestDisplayName, info.ExecutionTime, info.ExceptionType, info.ExceptionMessage, info.ExceptionStackTrace));
                runner.OnTestSkipped = info => writer.WriteAll(
                    session.RecordSkip(info.TypeName, info.TestDisplayName, info.SkipReason));
                runner.OnErrorMessage = info => writer.WriteAll(
                    session.RecordError(info.ExceptionType, info.ExceptionMessage));
                runner.OnExecutionComplete = _ => done.Set();

                // Empty TypesToRun runs the whole assembly, exactly like the plain runner's
                // parameterless Start (AssemblyRunnerStartOptions.Empty).
                runner.Start(new AssemblyRunnerStartOptions { TypesToRun = classes?.ToArray() ?? [] });

                // No overall deadline: every scenario is already bounded by Atlas's per-scenario
                // watchdog (60 s default, [AtlasScenario(TimeoutMs)] override).
                done.Wait();

                // The final class's host normally hands off at process exit, AFTER run-end has
                // closed the stream; shutting it down now (through the same seam `atlas
                // fixture` uses) moves that hand-off before the stream closes, so the last
                // class-summary still rides the protocol. Same total cost: the graceful
                // dispose only moves from process exit to here.
                ShutDownHostQuietly();
            }
            finally
            {
                // Bounded idle wait before Dispose; leaks the runner if it never idles.
                // Disposing a busy runner races its worker thread (the xunit 2.x AssemblyRunner
                // disposal race, issue #59): the resulting ObjectDisposedException would kill
                // this worker and be reported as a failed class, a lie about the class.
                RunnerDisposal.DisposeWhenIdle(runner);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= crashHandler;

            // Complete is idempotent: a no-op if the crash handler already closed the stream.
            writer.WriteAll(session.Complete(stopwatch.ElapsedMilliseconds));
        }

        return session.ExitCode;
    }

    /// <summary>Shuts the live scenario host down gracefully, best-effort: a shutdown failure
    /// after every scenario already reported must not fail the run, and the process-exit
    /// disposal remains the backstop. A missing seam (not an Atlas scenario assembly, or an
    /// older harness) is equally fine: there is then no host to shut down through it.</summary>
    private static void ShutDownHostQuietly()
    {
        try
        {
            FixtureHarvest.ShutDownAndHarvestSavePath(out _);
        }
        catch
        {
            // Best-effort by design; see the method summary.
        }
    }

    private static string AtlasVersion() =>
        typeof(WorkerRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(WorkerRunner).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
