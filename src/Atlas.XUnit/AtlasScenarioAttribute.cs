using Xunit;
using Xunit.Sdk;

namespace Atlas.XUnit;

/// <summary>Marks a test method as an Atlas scenario, run on the embedded game server's game thread.</summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasScenarioDiscoverer", "Atlas.XUnit")]
public sealed class AtlasScenarioAttribute : FactAttribute
{
    /// <summary>Gets or sets a value indicating whether the class host is recycled before this
    /// scenario runs, giving it a fresh world instead of the one shared by the test class.</summary>
    public bool FreshWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether the class host's world is rolled back to
    /// its snapshot before this scenario runs: the same clean-world slot in the lifecycle as
    /// <see cref="FreshWorld"/>, but without rebooting the server, at a small fraction of the
    /// cost. The snapshot is captured lazily, once per host, at the class's first
    /// rollback-enabled scenario (that scenario runs against the world as captured; later ones
    /// are rolled back to it), so classes that never opt in pay nothing.</summary>
    /// <remarks><para>What a rollback restores: blocks, block entities, chunk-stored entities,
    /// chunk moddata, savegame data (<c>SaveGame.ModData</c>, spawn, entity id counters) and the
    /// calendar, for dimension 0. What it does NOT restore: mod in-memory state that is not tied
    /// to chunk/entity lifecycle events (ModSystem fields, statics, caches) and in-memory map
    /// chunk state (height maps, map moddata), which the engine keeps preferring over the
    /// restored blobs. Scenarios sensitive to those need <see cref="FreshWorld"/>.</para>
    /// <para>Fail closed: if capture or restore fails for any reason (including engine drift in
    /// a future game version), Atlas logs a one-line warning to stderr and falls back to the
    /// <see cref="FreshWorld"/> full-recycle path, so the scenario still gets its clean world.
    /// Every degrade is also attached to the scenario's own test output (visible in the IDE test
    /// explorer, the TRX report and `atlas run`), with the reason and the fallback recycle cost,
    /// and each class gets an end-of-class isolation summary on stderr; scenarios that treat the
    /// speedup as a contract can fail instead via <see cref="StrictIsolation"/>. Test players
    /// are a hard limit instead: requesting a rollback on a class that has joined test players
    /// fails the scenario with an <c>AtlasSetupException</c>, because player entity state would
    /// not be rolled back (players + rollback is a later stage). Combining
    /// <see cref="RollbackWorld"/> with <see cref="FreshWorld"/> is a setup error: they
    /// contradict.</para></remarks>
    public bool RollbackWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether a degraded <see cref="RollbackWorld"/>
    /// request FAILS the scenario instead of silently falling back to a full host recycle:
    /// opt-in for suites that treat the rollback speedup as a contract. The failure is an
    /// <c>AtlasIsolationException</c> carrying the degrade reason (players joined,
    /// mini-dimension chunks loaded, engine drift, or a generic capture/restore failure).</summary>
    /// <remarks><para>The host is still recycled before the failure surfaces, so later scenarios
    /// of the class keep running on a clean world; strictness changes visibility, not safety.
    /// A genuine server crash during the rollback attempt is never re-labelled: it keeps
    /// surfacing as <c>ServerCrashedException</c>.</para>
    /// <para>Only meaningful together with <see cref="RollbackWorld"/>: setting it without
    /// <see cref="RollbackWorld"/> is a setup error (there is no rollback contract to be strict
    /// about; <see cref="FreshWorld"/> and shared-world scenarios cannot degrade).</para></remarks>
    public bool StrictIsolation { get; set; }

    /// <summary>Gets or sets the maximum time, in milliseconds, the scenario is allowed to run.</summary>
    /// <remarks>Deliberately does NOT map onto <see cref="FactAttribute.Timeout"/>: xUnit's own
    /// timeout path posts its <c>TestTimeoutException</c> continuation back through
    /// <c>SynchronizationContext.Current</c>, which for an Atlas scenario is the game thread's queue.
    /// If the game thread is the one that is stuck, that continuation never drains and the test hangs
    /// forever instead of failing at the timeout. This value flows to <c>AtlasTestCase</c> as plain
    /// data and is enforced by an off-thread <c>Watchdog</c> instead.</remarks>
    public int TimeoutMs { get; set; } = 60_000;
}
