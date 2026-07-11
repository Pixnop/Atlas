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
    public void FormatSummary_Should_BreakDegradesDownByReason_When_RollbacksDegraded()
    {
        var tally = new IsolationTally();
        tally.RecordRollback();
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded);
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded);
        tally.RecordDegrade(RollbackDegradeReason.CaptureOrRestoreFailed);
        tally.RecordFreshWorldRecycle();

        string summary = tally.FormatSummary("MyScenarios");

        Assert.Contains("1 rollback(s) succeeded", summary);
        Assert.Contains("3 degraded to a full host recycle", summary);
        Assert.Contains("capture or restore failed x1", summary);
        Assert.Contains("mini-dimension chunks loaded x2", summary);
        Assert.Contains("1 FreshWorld recycle(s)", summary);
    }

    [Fact]
    public void FormatSummary_Should_CountRestarts_When_RestartsWerePerformed()
    {
        var tally = new IsolationTally();
        tally.RecordRestart();
        tally.RecordRestart();

        string summary = tally.FormatSummary("MyMod.Tests.MyScenarios");

        Assert.Equal(
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 0 rollback(s) succeeded, " +
            "0 degraded to a full host recycle, 0 FreshWorld recycle(s), 2 restart(s).",
            summary);
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
            tally.RecordDegrade(RollbackDegradeReason.EngineDrift);
        }

        if (restarted)
        {
            tally.RecordRestart();
        }

        Assert.True(tally.HasReportableActivity);
    }

    [Fact]
    public void DrainSummary_Should_ReturnLineOnceAndForgetTheClass_When_LedgerHasActivity()
    {
        // The ledger is process-wide static state, so use a dedicated key type per test.
        IsolationLedger.RecordRollback(typeof(LedgerProbeA));
        IsolationLedger.RecordDegrade(typeof(LedgerProbeA), RollbackDegradeReason.PlayersJoined);

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
    public void DrainSummary_Should_ReturnLine_When_ClassOnlyRestarted()
    {
        IsolationLedger.RecordRestart(typeof(LedgerProbeC));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeC));

        Assert.NotNull(summary);
        Assert.Contains(typeof(LedgerProbeC).FullName!, summary);
        Assert.Contains("1 restart(s)", summary);
    }

    private sealed class LedgerProbeA;

    private sealed class LedgerProbeB;

    private sealed class LedgerProbeC;
}
