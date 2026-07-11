using Xunit.Runners;

namespace Atlas.Cli;

/// <summary>Implements `atlas run`: executes the scenarios of a compiled test assembly through an
/// in-process xunit runner, the same nested-runner pattern the engine's own E2E suite uses
/// (Atlas.Engine.Tests' NestedRunnerTests). One process, sequential (scenario assemblies disable
/// xunit parallelization themselves, exactly as under `dotnet test`), results streamed as they
/// arrive.</summary>
internal static class ScenarioRunner
{
    private static readonly object OutputLock = new();

    /// <summary>Runs the scenarios and streams per-scenario results to <paramref name="output"/>.</summary>
    /// <param name="assemblyPath">Path to the compiled scenario assembly.</param>
    /// <param name="filter">The display-name filter deciding which scenarios run.</param>
    /// <param name="output">Destination for per-scenario lines and the summary.</param>
    /// <returns>The process exit code (0 all passed, 1 otherwise).</returns>
    public static int Run(string assemblyPath, ScenarioFilter filter, TextWriter output)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        using var resolver = new ScenarioAssemblyResolver(Path.GetDirectoryName(fullPath)!);

        var report = new RunReport();
        using var done = new ManualResetEventSlim();

        var runner = AssemblyRunner.WithoutAppDomain(fullPath);
        try
        {
            runner.TestCaseFilter = testCase => filter.Matches(testCase.DisplayName);
            runner.OnTestPassed = info => WriteLine(
                output, report.RecordPass(info.TestDisplayName, info.ExecutionTime, info.Output));
            runner.OnTestFailed = info => WriteLine(
                output,
                report.RecordFail(
                    info.TestDisplayName,
                    info.ExecutionTime,
                    info.ExceptionType,
                    info.ExceptionMessage,
                    info.ExceptionStackTrace,
                    info.Output));
            runner.OnTestSkipped = info => WriteLine(output, report.RecordSkip(info.TestDisplayName, info.SkipReason));
            runner.OnErrorMessage = info => WriteLine(output, report.RecordError(info.ExceptionType, info.ExceptionMessage));
            runner.OnExecutionComplete = info =>
            {
                WriteLine(output, report.Summary(info.ExecutionTime));
                done.Set();
            };

            runner.Start();

            // No overall deadline: every scenario is already bounded by Atlas's own per-scenario
            // watchdog (60 s default, [AtlasScenario(TimeoutMs)] override), so the run cannot hang.
            done.Wait();
        }
        finally
        {
            // Bounded idle wait before Dispose; leaks the runner if it never idles. Disposing a
            // busy runner races its worker thread (the xunit 2.x AssemblyRunner disposal race,
            // issue #59) and the resulting ObjectDisposedException kills the process.
            RunnerDisposal.DisposeWhenIdle(runner);
        }

        return report.ExitCode;
    }

    private static void WriteLine(TextWriter output, string line)
    {
        lock (OutputLock)
        {
            output.WriteLine(line);
        }
    }
}
