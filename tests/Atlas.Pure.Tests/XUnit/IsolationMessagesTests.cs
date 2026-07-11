using Atlas.Internal.Rollback;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class IsolationMessagesTests
{
    private const string Detail = "InvalidOperationException: simulated capture failure";

    [Fact]
    public void DegradeReport_Should_CarryReasonDetailAndCost_When_Formatted()
    {
        string report = IsolationMessages.DegradeReport(
            RollbackDegradeReason.MiniDimensionChunksLoaded, Detail, TimeSpan.FromMilliseconds(8_400));

        Assert.StartsWith("[Atlas] world isolation degraded:", report, StringComparison.Ordinal);
        Assert.Contains("RollbackWorld fell back to a full host recycle", report);
        Assert.Contains("cost 8.4 s", report);
        Assert.Contains("Reason: mini-dimension chunks loaded.", report);
        Assert.Contains(Detail, report);
        Assert.DoesNotContain('\n', report); // single line: it must stay one output/stderr line
    }

    [Fact]
    public void DegradeReport_Should_UseInvariantDecimalPoint_When_FormattingCost()
    {
        string report = IsolationMessages.DegradeReport(
            RollbackDegradeReason.CaptureOrRestoreFailed, Detail, TimeSpan.FromMilliseconds(1_240));

        Assert.Contains("cost 1.2 s", report);
    }

    [Fact]
    public void RestartReport_Should_CarryCostAndExplainItsInvisibility_When_Formatted()
    {
        string report = IsolationMessages.RestartReport(TimeSpan.FromMilliseconds(7_060));

        Assert.StartsWith("[Atlas] world restarted:", report, StringComparison.Ordinal);
        Assert.Contains("RestartWorld", report);
        Assert.Contains("cost 7.1 s", report);
        Assert.Contains("paid outside the scenario's reported duration", report);
        Assert.DoesNotContain('\n', report); // single line: it must stay one output line
    }

    [Fact]
    public void StrictFailure_Should_NameScenarioReasonAndRemedy_When_Formatted()
    {
        string message = IsolationMessages.StrictFailure(
            "MyScenarios.Scenario_Should_RollBack", RollbackDegradeReason.PlayersJoined, Detail);

        Assert.Contains("'MyScenarios.Scenario_Should_RollBack'", message);
        Assert.Contains("StrictIsolation", message);
        Assert.Contains("Reason: players joined.", message);
        Assert.Contains(Detail, message);
        Assert.Contains("The host was recycled", message);
    }

    [Fact]
    public void RestartHarvestFailure_Should_NameClassPathAndSemantics_When_Formatted()
    {
        string message = IsolationMessages.RestartHarvestFailure(
            "MyMod.Tests.MyScenarios", "/tmp/atlas/abc/Saves/atlas.vcdbs");

        Assert.Contains("RestartWorld", message);
        Assert.Contains("'MyMod.Tests.MyScenarios'", message);
        Assert.Contains("'/tmp/atlas/abc/Saves/atlas.vcdbs'", message);
        Assert.Contains("did not persist a world save", message);
        Assert.Contains("never falls back silently", message);
    }

    [Fact]
    public void RestartPlayersJoinedFailure_Should_NameClassAndRemedies_When_Formatted()
    {
        string message = IsolationMessages.RestartPlayersJoinedFailure("MyMod.Tests.MyScenarios");

        Assert.Contains("RestartWorld", message);
        Assert.Contains("'MyMod.Tests.MyScenarios'", message);
        Assert.Contains("joined test players", message);
        Assert.Contains("connections die with the host", message);
        Assert.Contains("re-join", message);
        Assert.Contains("FreshWorld = true", message);
    }
}
