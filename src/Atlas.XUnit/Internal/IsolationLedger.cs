using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>Process-wide ledger of world-isolation outcomes, keyed by scenario class. The
/// registry and the invoker record outcomes as they happen; when a class hands its host off
/// (or the process exits), <see cref="HostRegistry"/> drains the class's summary and prints it
/// to stderr, so a suite that silently paid full recycles everywhere is visible at a glance.
/// Thread-safe, mirroring <see cref="HostRegistry"/>: scenario classes run sequentially, but
/// this class does not rely on that silently.</summary>
internal static class IsolationLedger
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, IsolationTally> Tallies = [];

    /// <summary>Counts one successful rollback for <paramref name="testClass"/>.</summary>
    /// <param name="testClass">The scenario class.</param>
    public static void RecordRollback(Type testClass)
    {
        lock (Gate)
        {
            TallyOf(testClass).RecordRollback();
        }
    }

    /// <summary>Counts one degraded rollback for <paramref name="testClass"/>.</summary>
    /// <param name="testClass">The scenario class.</param>
    /// <param name="reason">The structured degrade reason.</param>
    public static void RecordDegrade(Type testClass, RollbackDegradeReason reason)
    {
        lock (Gate)
        {
            TallyOf(testClass).RecordDegrade(reason);
        }
    }

    /// <summary>Counts one FreshWorld recycle for <paramref name="testClass"/>.</summary>
    /// <param name="testClass">The scenario class.</param>
    public static void RecordFreshWorldRecycle(Type testClass)
    {
        lock (Gate)
        {
            TallyOf(testClass).RecordFreshWorldRecycle();
        }
    }

    /// <summary>Counts one completed RestartWorld restart for <paramref name="testClass"/>.</summary>
    /// <param name="testClass">The scenario class.</param>
    public static void RecordRestart(Type testClass)
    {
        lock (Gate)
        {
            TallyOf(testClass).RecordRestart();
        }
    }

    /// <summary>Removes <paramref name="testClass"/>'s tally and formats its summary line.</summary>
    /// <param name="testClass">The scenario class whose host is being handed off.</param>
    /// <returns>The summary line, or <see langword="null"/> when the class never requested
    /// rollback or restart isolation (nothing could have degraded and nothing carried over, so
    /// a line would be noise).</returns>
    public static string? DrainSummary(Type testClass)
    {
        lock (Gate)
        {
            if (!Tallies.Remove(testClass, out IsolationTally? tally) || !tally.HasReportableActivity)
            {
                return null;
            }

            return tally.FormatSummary(testClass.FullName ?? testClass.Name);
        }
    }

    private static IsolationTally TallyOf(Type testClass)
    {
        if (!Tallies.TryGetValue(testClass, out IsolationTally? tally))
        {
            tally = new IsolationTally();
            Tallies[testClass] = tally;
        }

        return tally;
    }
}
