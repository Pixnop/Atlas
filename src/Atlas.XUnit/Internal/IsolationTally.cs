using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>Counts the world-isolation outcomes of one scenario class (snapshot captures,
/// rollbacks succeeded, rollbacks degraded to full recycles broken down by reason, FreshWorld
/// recycles, restarts) together with their measured wall-clock costs, and formats the
/// end-of-class summary line. Pure: <see cref="IsolationLedger"/> owns the per-class
/// bookkeeping; this type owns the counting and wording, so both are unit-testable.
/// Not thread-safe: the ledger serializes access under its own gate.</summary>
internal sealed class IsolationTally
{
    private readonly Dictionary<RollbackDegradeReason, int> _degrades = [];

    /// <summary>Gets the number of snapshot captures the class paid (the lazy first rollback
    /// request captures instead of restoring; a later capture only happens after a degrade
    /// discarded the snapshot). Counted separately from <see cref="RollbacksSucceeded"/> so N
    /// rollback scenarios read as 1 capture plus N-1 restores instead of a confusing N-1
    /// restore-cost lines (issue #71).</summary>
    public int Captures { get; private set; }

    /// <summary>Gets the number of rollback requests that restored the world to its snapshot
    /// (the lazy first capture is counted in <see cref="Captures"/>, not here).</summary>
    public int RollbacksSucceeded { get; private set; }

    /// <summary>Gets the number of rollback requests that degraded to a full host recycle.</summary>
    public int RollbacksDegraded { get; private set; }

    /// <summary>Gets the number of FreshWorld recycles the class requested.</summary>
    public int FreshWorldRecycles { get; private set; }

    /// <summary>Gets the number of RestartWorld restarts the class performed (a restart either
    /// works or fails the scenario hard, so every count here is a completed harvest-and-reboot).</summary>
    public int Restarts { get; private set; }

    /// <summary>Gets the accumulated wall-clock cost of the class's snapshot captures.</summary>
    public TimeSpan CaptureCostTotal { get; private set; }

    /// <summary>Gets the accumulated wall-clock cost of the class's successful restores.</summary>
    public TimeSpan RollbackCostTotal { get; private set; }

    /// <summary>Gets the accumulated wall-clock cost of the fallback recycles the class's
    /// degraded rollbacks paid.</summary>
    public TimeSpan DegradeCostTotal { get; private set; }

    /// <summary>Gets the accumulated wall-clock cost (dispose + boot) of the class's FreshWorld
    /// recycles. Like restarts, a recycle runs outside the scenario's timed body, so without
    /// this total a FreshWorld-only class of fast-looking PASS lines hides its paid boots.</summary>
    public TimeSpan FreshWorldCostTotal { get; private set; }

    /// <summary>Gets the accumulated wall-clock cost (shutdown + harvest + boot) of the class's
    /// completed restarts. Restarts run outside the scenario's timed body, so without this line
    /// a class of fast-looking PASS lines can hide many seconds of paid boots.</summary>
    public TimeSpan RestartCostTotal { get; private set; }

    /// <summary>Gets a value indicating whether the class ran ANY isolation mode at least once:
    /// only then is a summary worth printing. Since issue #71 this includes FreshWorld-only
    /// classes, which previously paid full recycles invisibly.</summary>
    public bool HasReportableActivity
        => Captures + RollbacksSucceeded + RollbacksDegraded + FreshWorldRecycles + Restarts > 0;

    /// <summary>Counts one snapshot capture (a rollback request served by capturing).</summary>
    /// <param name="cost">Wall-clock cost of the capture.</param>
    public void RecordCapture(TimeSpan cost)
    {
        Captures++;
        CaptureCostTotal += cost;
    }

    /// <summary>Counts one successful rollback (a restore to the snapshot).</summary>
    /// <param name="cost">Wall-clock cost of the restore.</param>
    public void RecordRollback(TimeSpan cost)
    {
        RollbacksSucceeded++;
        RollbackCostTotal += cost;
    }

    /// <summary>Counts one rollback degraded to a full host recycle.</summary>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="recycleCost">Wall-clock cost of the fallback recycle the degrade paid.</param>
    public void RecordDegrade(RollbackDegradeReason reason, TimeSpan recycleCost)
    {
        RollbacksDegraded++;
        DegradeCostTotal += recycleCost;
        _degrades[reason] = _degrades.GetValueOrDefault(reason) + 1;
    }

    /// <summary>Counts one FreshWorld recycle.</summary>
    /// <param name="cost">Wall-clock cost of the recycle (dispose + boot).</param>
    public void RecordFreshWorldRecycle(TimeSpan cost)
    {
        FreshWorldRecycles++;
        FreshWorldCostTotal += cost;
    }

    /// <summary>Counts one completed RestartWorld restart.</summary>
    /// <param name="cost">Wall-clock cost of the restart (shutdown + harvest + boot).</param>
    public void RecordRestart(TimeSpan cost)
    {
        Restarts++;
        RestartCostTotal += cost;
    }

    /// <summary>Formats the end-of-class summary line.</summary>
    /// <param name="className">The scenario class's display name.</param>
    /// <returns>The summary line.</returns>
    public string FormatSummary(string className)
        => $"[Atlas] isolation summary for {className}: {CapturePart()}, " +
           $"{RollbacksSucceeded} rollback(s) succeeded{CostSuffix(RollbacksSucceeded, RollbackCostTotal)}, " +
           $"{RollbacksDegraded} degraded to a full host recycle{DegradeBreakdown()}, " +
           $"{FreshWorldRecycles} FreshWorld recycle(s){CostSuffix(FreshWorldRecycles, FreshWorldCostTotal)}, " +
           $"{Restarts} restart(s){CostSuffix(Restarts, RestartCostTotal)}.";

    /// <summary>Formats the capture line item. The usual case is exactly one capture per class,
    /// so it reads "1 capture (1.2 s)" and the rollback arithmetic next to it is
    /// self-explanatory; several captures (possible after a degrade discarded the snapshot)
    /// fall back to the same total-cost shape as the other items.</summary>
    /// <returns>The capture part of the summary line.</returns>
    private string CapturePart() => Captures switch
    {
        0 => "0 captures",
        1 => $"1 capture ({IsolationMessages.FormatSeconds(CaptureCostTotal)})",
        _ => $"{Captures} captures ({IsolationMessages.FormatSeconds(CaptureCostTotal)} total)",
    };

    private string DegradeBreakdown()
    {
        if (_degrades.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> parts = _degrades
            .OrderBy(entry => entry.Key)
            .Select(entry => $"{RollbackDegrade.Describe(entry.Key)} x{entry.Value}");
        return $" ({string.Join(", ", parts)}; {IsolationMessages.FormatSeconds(DegradeCostTotal)} total)";
    }

    private static string CostSuffix(int count, TimeSpan costTotal)
        => count == 0 ? string.Empty : $" ({IsolationMessages.FormatSeconds(costTotal)} total)";
}
