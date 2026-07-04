using System.Collections.Concurrent;
using Xunit.Runners;

namespace Atlas.Engine.Tests;

/// <summary>Runs the Atlas.GuineaPig.Scenarios assembly (deliberately-failing scenarios, never
/// executed by a normal <c>dotnet test</c>) through an in-process xunit runner, and asserts
/// that every failure has exactly its documented shape. This is the only way to E2E-test
/// failure paths of the xUnit adapter itself - a test cannot assert its own failure - and it
/// covers the three paths of issue #11: the wall-clock watchdog firing through
/// <c>[AtlasScenario(TimeoutMs)]</c>, the invoker-driven dead-host fail-fast, and
/// <c>[AtlasScenario]</c> on a class not deriving from <c>AtlasScenarioBase</c>.</summary>
/// <remarks>The nested run boots real embedded servers inside this same process, one per guinea
/// pig class, sequentially (the guinea pig assembly disables parallelization, and this suite
/// runs one test at a time) - so the one-live-server constraint holds throughout.</remarks>
[Trait("Category", "E2E")]
public class NestedRunnerTests
{
    [Fact]
    public async Task GuineaPigSuite_Should_FailInTheDocumentedWays_When_RunNested()
    {
        // Assembly.Location, not AppContext.BaseDirectory: the first host boot in the process
        // redirects BaseDirectory to the game install, and earlier tests in a full run have
        // already booted hosts by the time this one executes.
        string dll = Path.Combine(
            Path.GetDirectoryName(typeof(NestedRunnerTests).Assembly.Location)!,
            "Atlas.GuineaPig.Scenarios.dll");
        Assert.True(File.Exists(dll), $"Guinea pig assembly not found at '{dll}'.");

        var failures = new ConcurrentDictionary<string, string>();
        var passedNames = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var runner = AssemblyRunner.WithoutAppDomain(dll);
        runner.OnTestFailed = info => failures[info.MethodName] = $"{info.ExceptionType}: {info.ExceptionMessage}\n{info.ExceptionStackTrace}";
        runner.OnTestPassed = info => passedNames.Enqueue(info.MethodName);
        runner.OnExecutionComplete = _ => done.TrySetResult();
        runner.Start();

        // Generous bound: three server boots plus one deliberate 8-second game-thread wedge.
        await done.Task.WaitAsync(TimeSpan.FromMinutes(4));

        Assert.True(passedNames.IsEmpty, "Guinea pig scenarios unexpectedly passed: " + string.Join(", ", passedNames));
        Assert.True(
            failures.Count == 4,
            "Expected 4 failures, got:\n" + string.Join("\n----\n", failures.Select(f => f.Key + " => " + f.Value)));

        // Path 1 (#11): the wall-clock watchdog fires through [AtlasScenario(TimeoutMs)].
        string hang = failures["Scenario_Should_TimeOut_When_GameThreadWedges"];
        Assert.Contains("ScenarioTimeoutException", hang);
        Assert.Contains("2000 ms", hang);

        // Path 2 (#11): the crash surfaces, then the next scenario on the same class host fails
        // fast instead of hanging or rebooting. The crashing scenario's own await continuation
        // dies with the game thread, so the watchdog is what recovers it (marking the host
        // abandoned) and WrapCrashIfAny surfaces the true crash - possibly aggregated with the
        // same crash observed a second time through xUnit's async-test sync context.
        string crash = failures["A_Scenario_Should_Crash_When_PoisonCallbackKillsThePump"];
        Assert.Contains("Embedded server died", crash);

        string failFast = failures["B_Scenario_Should_FailFast_When_ClassHostAlreadyCrashed"];
        Assert.Contains("ServerCrashedException", failFast);
        Assert.Contains("host was abandoned after a scenario exceeded its 5000 ms watchdog", failFast);

        // Path 3 (#11): [AtlasScenario] on a class not deriving from AtlasScenarioBase.
        string notDerived = failures["Scenario_Should_FailSetup_When_ClassDoesNotDeriveFromBase"];
        Assert.Contains("AtlasSetupException", notDerived);
        Assert.Contains("must derive from AtlasScenarioBase", notDerived);
    }
}
