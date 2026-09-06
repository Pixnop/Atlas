using System.Collections.Concurrent;
using Atlas.Cli;
using Xunit.Runners;

namespace Atlas.Engine.Tests.Support;

/// <summary>Runs classes of the Atlas.GuineaPig.Scenarios assembly (deliberately-failing
/// scenarios, never executed by a normal <c>dotnet test</c>) through an in-process xunit runner,
/// which is also exactly how the atlas CLI executes scenarios. This is the only way to E2E-test
/// the failure paths of the xUnit adapter itself: a test cannot assert its own failure.</summary>
/// <remarks>The nested run boots real embedded servers inside this same process, one per guinea
/// pig class, sequentially (the guinea pig assembly disables parallelization, and this suite runs
/// one test at a time), so the one-live-server constraint holds throughout.</remarks>
internal static class GuineaPigRunner
{
    /// <summary>The guinea pig assembly's namespace, which is also its assembly name.</summary>
    public const string Namespace = "Atlas.GuineaPig.Scenarios";

    /// <summary>Runs the named guinea pig classes and collects every scenario's outcome.</summary>
    /// <param name="classNames">Simple names of the guinea pig classes to run.</param>
    /// <param name="timeout">Bound on the whole nested run.</param>
    /// <param name="preEnumerateTheories">Whether theory rows become one test case each at
    /// discovery time; <see langword="null"/> leaves xunit's own default in place.</param>
    /// <param name="displayNameFilter">Optional display-name filter selecting scenarios.</param>
    /// <returns>One entry per scenario the run reported, in completion order.</returns>
    public static async Task<IReadOnlyList<ScenarioOutcome>> RunAsync(
        IReadOnlyList<string> classNames,
        TimeSpan timeout,
        bool? preEnumerateTheories = null,
        Func<string, bool>? displayNameFilter = null)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        string dll = TestPaths.GuineaPigDll;
        Assert.True(File.Exists(dll), $"Guinea pig assembly not found at '{dll}'.");

        var outcomes = new ConcurrentQueue<ScenarioOutcome>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = AssemblyRunner.WithoutAppDomain(dll);
        try
        {
            if (displayNameFilter != null)
            {
                runner.TestCaseFilter = testCase => displayNameFilter(testCase.DisplayName);
            }

            runner.OnTestPassed = info =>
                outcomes.Enqueue(new ScenarioOutcome(info.MethodName, info.TestDisplayName, null));
            runner.OnTestFailed = info => outcomes.Enqueue(new ScenarioOutcome(
                info.MethodName,
                info.TestDisplayName,
                $"{info.ExceptionType}: {info.ExceptionMessage}\n{info.ExceptionStackTrace}"));
            runner.OnExecutionComplete = _ => done.TrySetResult();
            runner.Start(new AssemblyRunnerStartOptions
            {
                PreEnumerateTheories = preEnumerateTheories,
                TypesToRun = [.. classNames.Select(name => $"{Namespace}.{name}")],
            });

            // A timeout here is never a slow boot: it means the nested run hung (as it did when
            // the registry reused a superseded host, see SupersededHostTests), so widening the
            // bound only delays the failure.
            await done.Task.WaitAsync(timeout);
        }
        finally
        {
            // Not a `using`: disposing the runner while its worker thread is still heading into
            // its final wait is the xunit 2.x disposal race that killed a whole CI leg with an
            // unhandled ObjectDisposedException (issue #59). DisposeWhenIdle waits for Idle
            // (bounded) and prefers leaking the runner over disposing it hot.
            RunnerDisposal.DisposeWhenIdle(runner);
        }

        return [.. outcomes];
    }
}
