using Atlas.Internal.Rollback;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class IsolationTallyTests
{
    [Fact]
    public void FormatSummary_Should_ReportCounts_When_OnlyRollbacksSucceeded()
    {
        var tally = new IsolationTally();
        tally.RecordRollback();
        tally.RecordRollback();

        string summary = tally.FormatSummary("MyMod.Tests.MyScenarios");

        Assert.Equal(
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 2 rollback(s) succeeded, " +
            "0 degraded to a full host recycle, 0 FreshWorld recycle(s), 0 restart(s).",
            summary);
    }

    [Fact]
    public void FormatSummary_Should_BreakDegradesDownByReasonWithTotalCost_When_RollbacksDegraded()
    {
        var tally = new IsolationTally();
        tally.RecordRollback();
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded, TimeSpan.FromSeconds(2.5));
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded, TimeSpan.FromSeconds(1.5));
        tally.RecordDegrade(RollbackDegradeReason.CaptureOrRestoreFailed, TimeSpan.FromSeconds(2.9));
        tally.RecordFreshWorldRecycle();

        string summary = tally.FormatSummary("MyScenarios");

        Assert.Contains("1 rollback(s) succeeded", summary);
        Assert.Contains("3 degraded to a full host recycle", summary);
        Assert.Contains("capture or restore failed x1", summary);
        Assert.Contains("mini-dimension chunks loaded x2", summary);
        Assert.Contains("; 6.9 s total)", summary);
        Assert.Contains("1 FreshWorld recycle(s)", summary);
        Assert.Equal(TimeSpan.FromSeconds(6.9), tally.DegradeCostTotal);
    }

    [Fact]
    public void FormatSummary_Should_CountRestartsWithTotalCost_When_RestartsWerePerformed()
    {
        var tally = new IsolationTally();
        tally.RecordRestart(TimeSpan.FromSeconds(7.0));
        tally.RecordRestart(TimeSpan.FromSeconds(7.1));

        string summary = tally.FormatSummary("MyMod.Tests.MyScenarios");

        Assert.Equal(
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 0 rollback(s) succeeded, " +
            "0 degraded to a full host recycle, 0 FreshWorld recycle(s), 2 restart(s) (14.1 s total).",
            summary);
        Assert.Equal(TimeSpan.FromSeconds(14.1), tally.RestartCostTotal);
    }

    [Fact]
    public void HasReportableActivity_Should_BeFalse_When_OnlyFreshWorldRecyclesHappened()
    {
        var tally = new IsolationTally();
        tally.RecordFreshWorldRecycle();

        Assert.False(tally.HasReportableActivity);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void HasReportableActivity_Should_BeTrue_When_AnyRollbackOrRestartWasRequested(
        bool succeeded, bool degraded, bool restarted)
    {
        var tally = new IsolationTally();
        if (succeeded)
        {
            tally.RecordRollback();
        }

        if (degraded)
        {
            tally.RecordDegrade(RollbackDegradeReason.EngineDrift, TimeSpan.FromSeconds(1));
        }

        if (restarted)
        {
            tally.RecordRestart(TimeSpan.FromSeconds(7));
        }

        Assert.True(tally.HasReportableActivity);
    }

    [Fact]
    public void DrainSummary_Should_ReturnLineOnceAndForgetTheClass_When_LedgerHasActivity()
    {
        // The ledger is process-wide static state, so use a dedicated key type per test.
        IsolationLedger.RecordRollback(typeof(LedgerProbeA));
        IsolationLedger.RecordDegrade(typeof(LedgerProbeA), RollbackDegradeReason.PlayersJoined, TimeSpan.FromSeconds(3));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeA));

        Assert.NotNull(summary);
        Assert.Contains(typeof(LedgerProbeA).FullName!, summary);
        Assert.Contains("1 rollback(s) succeeded", summary);
        Assert.Contains("players joined x1", summary);
        Assert.Null(IsolationLedger.DrainSummary(typeof(LedgerProbeA))); // drained means gone
    }

    [Fact]
    public void DrainSummary_Should_ReturnNull_When_ClassNeverRequestedRollbackOrRestart()
    {
        Assert.Null(IsolationLedger.DrainSummary(typeof(LedgerProbeB)));

        IsolationLedger.RecordFreshWorldRecycle(typeof(LedgerProbeB));
        Assert.Null(IsolationLedger.DrainSummary(typeof(LedgerProbeB)));
    }

    [Fact]
    public void DrainSummary_Should_ReturnLineWithRestartCost_When_ClassOnlyRestarted()
    {
        IsolationLedger.RecordRestart(typeof(LedgerProbeC), TimeSpan.FromSeconds(7.3));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeC));

        Assert.NotNull(summary);
        Assert.Contains(typeof(LedgerProbeC).FullName!, summary);
        Assert.Contains("1 restart(s) (7.3 s total)", summary);
    }

    private sealed class LedgerProbeA;

    private sealed class LedgerProbeB;

    private sealed class LedgerProbeC;
}
