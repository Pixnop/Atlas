using Atlas.Cli;

namespace Atlas.Engine.Tests;

/// <summary>Drives the atlas CLI's lister and runner against the Atlas.GuineaPig.Scenarios
/// assembly copied into this project's output (the same nested-runner setup as
/// <see cref="NestedRunnerTests"/>). Kept in the E2E category because the suite exercises the
/// real xunit discovery/execution machinery on a real scenario assembly, even though neither
/// test below boots a server: listing never executes anything, and the run test filters to the
/// one guinea pig that fails its setup guard before any host is created.</summary>
[Trait("Category", "E2E")]
public class CliFacadeTests
{
    private static string GuineaPigDll => Path.Combine(
        Path.GetDirectoryName(typeof(CliFacadeTests).Assembly.Location)!,
        "Atlas.GuineaPig.Scenarios.dll");

    [Fact]
    public void List_Should_PrintEveryScenario_When_NoFilterGiven()
    {
        var output = new StringWriter();

        int exitCode = ScenarioLister.List(GuineaPigDll, new ScenarioFilter(null), output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Scenario_Should_TimeOut_When_GameThreadWedges", text);
        Assert.Contains("A_Scenario_Should_Crash_When_PoisonCallbackKillsThePump", text);
        Assert.Contains("B_Scenario_Should_FailFast_When_ClassHostAlreadyCrashed", text);
        Assert.Contains("Scenario_Should_FailSetup_When_ClassDoesNotDeriveFromBase", text);
        Assert.Contains("Scenario_Should_FailSetup_When_FreshWorldAndRollbackWorldAreCombined", text);
        Assert.Contains("A_Scenario_Should_Pass_When_RollbackWorldIsRequested", text);
        Assert.Contains("B_Scenario_Should_Pass_When_RestartWorldIsRequested", text);
        Assert.Contains("Discovered: 7", text);
    }

    [Fact]
    public void List_Should_PrintOnlyMatches_When_FilterGiven()
    {
        var output = new StringWriter();

        int exitCode = ScenarioLister.List(GuineaPigDll, new ScenarioFilter("NotDerived"), output);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Scenario_Should_FailSetup_When_ClassDoesNotDeriveFromBase", text);
        Assert.DoesNotContain("GameThreadWedges", text);
        Assert.Contains("Discovered: 1", text);
    }

    [Fact]
    public void Run_Should_ReportFailureAndExitNonZero_When_FilteredScenarioFails()
    {
        var output = new StringWriter();

        int exitCode = ScenarioRunner.Run(GuineaPigDll, new ScenarioFilter("ClassDoesNotDeriveFromBase"), output);

        string text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL", text);
        Assert.Contains("AtlasSetupException", text);
        Assert.Contains("must derive from AtlasScenarioBase", text);
        Assert.Contains("Total: 1, Passed: 0, Failed: 1, Skipped: 0", text);
    }

    [Fact]
    public void Run_Should_ExitNonZero_When_FilterMatchesNothing()
    {
        var output = new StringWriter();

        int exitCode = ScenarioRunner.Run(GuineaPigDll, new ScenarioFilter("NoSuchScenarioAnywhere"), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("No scenarios ran", output.ToString());
    }
}
