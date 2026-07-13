using Atlas.Internal.Rollback;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class IsolationTallyTests
{
    [Fact]
    public void FormatSummary_Should_ReportCaptureAndRestoresSeparately_When_RollbacksSucceeded()
    {
        // Three rollback scenarios: the lazy first request captures, the next two restore. The
        // capture is its own line item with its own cost, so the arithmetic is
        // self-explanatory instead of N scenarios producing N-1 restore lines (issue #71).
        var tally = new IsolationTally();
        tally.RecordCapture(TimeSpan.FromSeconds(1.2));
        tally.RecordRollback(TimeSpan.FromSeconds(0.2));
        tally.RecordRollback(TimeSpan.FromSeconds(0.2));

        string summary = tally.FormatSummary("MyMod.Tests.MyScenarios");

        Assert.Equal(
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 1 capture (1.2 s), " +
            "2 rollback(s) succeeded (0.4 s total), 0 degraded to a full host recycle, " +
            "0 FreshWorld recycle(s), 0 restart(s).",
            summary);
        Assert.Equal(TimeSpan.FromSeconds(1.2), tally.CaptureCostTotal);
        Assert.Equal(TimeSpan.FromSeconds(0.4), tally.RollbackCostTotal);
    }

    [Fact]
    public void FormatSummary_Should_TotalTheCaptureCosts_When_ADegradeForcedASecondCapture()
    {
        // A degrade discards the snapshot, so the next rollback request captures again: the
        // plural shape totals the capture costs like every other line item.
        var tally = new IsolationTally();
        tally.RecordCapture(TimeSpan.FromSeconds(1.0));
        tally.RecordCapture(TimeSpan.FromSeconds(1.4));

        string summary = tally.FormatSummary("MyScenarios");

        Assert.Contains("2 captures (2.4 s total)", summary);
    }

    [Fact]
    public void FormatSummary_Should_BreakDegradesDownByReasonWithTotalCost_When_RollbacksDegraded()
    {
        var tally = new IsolationTally();
        tally.RecordRollback(TimeSpan.FromSeconds(0.3));
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded, TimeSpan.FromSeconds(2.5));
        tally.RecordDegrade(RollbackDegradeReason.MiniDimensionChunksLoaded, TimeSpan.FromSeconds(1.5));
        tally.RecordDegrade(RollbackDegradeReason.CaptureOrRestoreFailed, TimeSpan.FromSeconds(2.9));
        tally.RecordFreshWorldRecycle(TimeSpan.FromSeconds(7.2));

        string summary = tally.FormatSummary("MyScenarios");

        Assert.Contains("0 captures", summary);
        Assert.Contains("1 rollback(s) succeeded (0.3 s total)", summary);
        Assert.Contains("3 degraded to a full host recycle", summary);
        Assert.Contains("capture or restore failed x1", summary);
        Assert.Contains("mini-dimension chunks loaded x2", summary);
        Assert.Contains("; 6.9 s total)", summary);
        Assert.Contains("1 FreshWorld recycle(s) (7.2 s total)", summary);
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
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 0 captures, " +
            "0 rollback(s) succeeded, 0 degraded to a full host recycle, " +
            "0 FreshWorld recycle(s), 2 restart(s) (14.1 s total).",
            summary);
        Assert.Equal(TimeSpan.FromSeconds(14.1), tally.RestartCostTotal);
    }

    [Fact]
    public void FormatSummary_Should_ReportTheRecycleCost_When_OnlyFreshWorldRecyclesHappened()
    {
        // The issue #71 gap: a FreshWorld-only class pays one full boot per scenario and used
        // to stay silent; its summary now carries the count and the measured total.
        var tally = new IsolationTally();
        tally.RecordFreshWorldRecycle(TimeSpan.FromSeconds(7.0));
        tally.RecordFreshWorldRecycle(TimeSpan.FromSeconds(7.2));

        string summary = tally.FormatSummary("MyMod.Tests.MyScenarios");

        Assert.Equal(
            "[Atlas] isolation summary for MyMod.Tests.MyScenarios: 0 captures, " +
            "0 rollback(s) succeeded, 0 degraded to a full host recycle, " +
            "2 FreshWorld recycle(s) (14.2 s total), 0 restart(s).",
            summary);
        Assert.Equal(TimeSpan.FromSeconds(14.2), tally.FreshWorldCostTotal);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void HasReportableActivity_Should_BeTrue_When_AnyIsolationModeRan(
        bool captured, bool succeeded, bool degraded, bool freshWorld, bool restarted)
    {
        var tally = new IsolationTally();
        Assert.False(tally.HasReportableActivity);
        if (captured)
        {
            tally.RecordCapture(TimeSpan.FromSeconds(1));
        }

        if (succeeded)
        {
            tally.RecordRollback(TimeSpan.FromSeconds(0.2));
        }

        if (degraded)
        {
            tally.RecordDegrade(RollbackDegradeReason.EngineDrift, TimeSpan.FromSeconds(1));
        }

        if (freshWorld)
        {
            tally.RecordFreshWorldRecycle(TimeSpan.FromSeconds(7));
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
        IsolationLedger.RecordRollback(typeof(LedgerProbeA), TimeSpan.FromSeconds(0.2));
        IsolationLedger.RecordDegrade(typeof(LedgerProbeA), RollbackDegradeReason.PlayersJoined, TimeSpan.FromSeconds(3));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeA));

        Assert.NotNull(summary);
        Assert.Contains(typeof(LedgerProbeA).FullName!, summary);
        Assert.Contains("1 rollback(s) succeeded (0.2 s total)", summary);
        Assert.Contains("players joined x1", summary);
        Assert.Null(IsolationLedger.DrainSummary(typeof(LedgerProbeA))); // drained means gone
    }

    [Fact]
    public void DrainSummary_Should_ReturnNull_When_ClassNeverRanAnyIsolationMode()
    {
        Assert.Null(IsolationLedger.DrainSummary(typeof(LedgerProbeB)));
    }

    [Fact]
    public void DrainSummary_Should_ReturnLineWithRecycleCost_When_ClassOnlyUsedFreshWorld()
    {
        // Pre-#71 this returned null and the class's paid recycles were invisible.
        IsolationLedger.RecordFreshWorldRecycle(typeof(LedgerProbeD), TimeSpan.FromSeconds(7.4));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeD));

        Assert.NotNull(summary);
        Assert.Contains(typeof(LedgerProbeD).FullName!, summary);
        Assert.Contains("1 FreshWorld recycle(s) (7.4 s total)", summary);
    }

    [Fact]
    public void DrainSummary_Should_ReturnLineWithCaptureCost_When_ClassOnlyCaptured()
    {
        // A single rollback scenario captures and never restores: the summary still explains
        // what the class paid.
        IsolationLedger.RecordCapture(typeof(LedgerProbeE), TimeSpan.FromSeconds(1.2));

        string? summary = IsolationLedger.DrainSummary(typeof(LedgerProbeE));

        Assert.NotNull(summary);
        Assert.Contains("1 capture (1.2 s)", summary);
        Assert.Contains("0 rollback(s) succeeded", summary);
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

    private sealed class LedgerProbeD;

    private sealed class LedgerProbeE;
}
