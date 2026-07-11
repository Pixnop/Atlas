using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>Counts the world-isolation outcomes of one scenario class (rollbacks succeeded,
/// rollbacks degraded to full recycles broken down by reason, FreshWorld recycles) and formats
/// the end-of-class summary line. Pure: <see cref="IsolationLedger"/> owns the per-class
/// bookkeeping; this type owns the counting and wording, so both are unit-testable. Not
/// thread-safe: the ledger serializes access under its own gate.</summary>
internal sealed class IsolationTally
{
    private readonly Dictionary<RollbackDegradeReason, int> _degrades = [];

    /// <summary>Gets the number of rollback requests that succeeded (the lazy first capture
    /// included).</summary>
    public int RollbacksSucceeded { get; private set; }

    /// <summary>Gets the number of rollback requests that degraded to a full host recycle.</summary>
    public int RollbacksDegraded { get; private set; }

    /// <summary>Gets the number of FreshWorld recycles the class requested.</summary>
    public int FreshWorldRecycles { get; private set; }

    /// <summary>Gets a value indicating whether the class requested rollback isolation at least
    /// once: only then is a summary worth printing (FreshWorld-only classes have nothing that
    /// can degrade).</summary>
    public bool HasRollbackActivity => RollbacksSucceeded + RollbacksDegraded > 0;

    /// <summary>Counts one successful rollback.</summary>
    public void RecordRollback() => RollbacksSucceeded++;

    /// <summary>Counts one rollback degraded to a full host recycle.</summary>
    /// <param name="reason">The structured degrade reason.</param>
    public void RecordDegrade(RollbackDegradeReason reason)
    {
        RollbacksDegraded++;
        _degrades[reason] = _degrades.GetValueOrDefault(reason) + 1;
    }

    /// <summary>Counts one FreshWorld recycle.</summary>
    public void RecordFreshWorldRecycle() => FreshWorldRecycles++;

    /// <summary>Formats the end-of-class summary line.</summary>
    /// <param name="className">The scenario class's display name.</param>
    /// <returns>The summary line.</returns>
    public string FormatSummary(string className)
        => $"[Atlas] isolation summary for {className}: {RollbacksSucceeded} rollback(s) succeeded, " +
           $"{RollbacksDegraded} degraded to a full host recycle{DegradeBreakdown()}, " +
           $"{FreshWorldRecycles} FreshWorld recycle(s).";

    private string DegradeBreakdown()
    {
        if (_degrades.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> parts = _degrades
            .OrderBy(entry => entry.Key)
            .Select(entry => $"{RollbackDegrade.Describe(entry.Key)} x{entry.Value}");
        return $" ({string.Join(", ", parts)})";
    }
}
