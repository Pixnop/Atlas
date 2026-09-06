namespace Atlas.Engine.Tests;

/// <summary>Runs the issue-#11 classes of the Atlas.GuineaPig.Scenarios assembly through the
/// nested runner (<see cref="GuineaPigRunner"/>) and asserts that every failure has exactly its
/// documented shape. It covers the three paths of issue #11: the wall-clock watchdog firing
/// through <c>[AtlasScenario(TimeoutMs)]</c>, the invoker-driven dead-host fail-fast, and
/// <c>[AtlasScenario]</c> on a class not deriving from <c>AtlasScenarioBase</c>. Other guinea
/// pig classes have their own nested-runner tests (<c>TheoryNestedRunnerTests</c>), so this run
/// is scoped to its five classes.</summary>
[Trait("Category", "E2E")]
public class NestedRunnerTests
{
    [Fact]
    public async Task GuineaPigSuite_Should_FailInTheDocumentedWays_When_RunNested()
    {
        // Generous bound: five server boots (the isolation-activity class included; its extra
        // rollback scenario costs no boot) plus one deliberate 8-second game-thread wedge.
        IReadOnlyList<ScenarioOutcome> outcomes = await GuineaPigRunner.RunAsync(
            [
                "HangingScenarios",
                "DeadHostSequenceScenarios",
                "NotDerivedScenarios",
                "ConflictingIsolationScenarios",
                "IsolationActivityScenarios"
            ],
            TimeSpan.FromMinutes(4));

        // Grouped, not keyed directly: a duplicate method name must not throw before the count
        // assertion below can print its dump, which is the only diagnostic this test has.
        Dictionary<string, string> failures = outcomes
            .Where(outcome => !outcome.Passed)
            .GroupBy(outcome => outcome.MethodName)
            .ToDictionary(group => group.Key, group => group.Last().Failure!);

        // The isolation-activity guinea pigs are the assembly's only passing scenarios (they
        // exist to produce real capture/rollback/restart activity for the summary tests: the
        // first rollback scenario captures, the second restores).
        Assert.Equal(
            [
                "A_Scenario_Should_Pass_When_RollbackWorldIsRequested",
                "A_Scenario_Should_Pass_When_RollbackWorldRestores",
                "B_Scenario_Should_Pass_When_RestartWorldIsRequested",
            ],
            outcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.MethodName).Order().ToList());
        Assert.True(
            failures.Count == 5,
            "Expected 5 failures, got:\n" + string.Join("\n----\n", failures.Select(f => f.Key + " => " + f.Value)));

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

        // Contradictory isolation: FreshWorld + RollbackWorld on one scenario is a setup error,
        // surfaced by the resolver before any host is booted (so this failure costs no boot).
        string conflict = failures["Scenario_Should_FailSetup_When_FreshWorldAndRollbackWorldAreCombined"];
        Assert.Contains("AtlasSetupException", conflict);
        Assert.Contains("FreshWorld", conflict);
        Assert.Contains("RollbackWorld", conflict);
    }
}
