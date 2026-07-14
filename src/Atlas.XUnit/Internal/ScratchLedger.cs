namespace Atlas.XUnit.Internal;

/// <summary>Process-wide record of which scenario classes have observed a failure, feeding the
/// scratch sweep's keep-or-delete decision (issue #83): <see cref="AtlasTestRunner"/> records a
/// failure the moment a scenario's exceptions are aggregated, and <see cref="HostRegistry"/>
/// consults the record when it disposes a host, so from a class's first red scenario on, every
/// scratch directory the class touches is kept as post-mortem evidence. Failures are only ever
/// added, never cleared: a class that failed once in this process keeps its later hosts'
/// scratch too (a rerun in a fresh process starts clean). Thread-safe, mirroring
/// <see cref="IsolationLedger"/>: scenario classes run sequentially, but this class does not
/// rely on that silently.</summary>
internal static class ScratchLedger
{
    private static readonly object Gate = new();
    private static readonly HashSet<Type> FailedClasses = [];

    /// <summary>Records that a scenario of <paramref name="testClass"/> failed.</summary>
    /// <param name="testClass">The scenario class the failing scenario belongs to.</param>
    public static void RecordFailure(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        lock (Gate)
        {
            FailedClasses.Add(testClass);
        }
    }

    /// <summary>Reads whether any scenario of <paramref name="testClass"/> has failed so far.</summary>
    /// <param name="testClass">The scenario class owning the host being disposed.</param>
    /// <returns><see langword="true"/> when the class has at least one recorded failure.</returns>
    public static bool HasObservedFailure(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        lock (Gate)
        {
            return FailedClasses.Contains(testClass);
        }
    }
}
