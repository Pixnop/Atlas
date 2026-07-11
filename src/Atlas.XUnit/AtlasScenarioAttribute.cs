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
    /// calendar, for EVERY dimension (mini-dimension chunk columns round-trip through the
    /// snapshot since stage 3, boot-time pregenerated ones included); and, for joined test
    /// players, their captured state: position, watched attributes (health, saturation, custom
    /// mod trees), inventories and per-player moddata. Players that joined AFTER the snapshot
    /// was captured are removed by the rollback (the world returns exactly to its captured
    /// population); their names are freed, so a rollback scenario can rejoin them as brand-new
    /// players. What it does NOT restore: mod in-memory state that is not tied to chunk/entity
    /// lifecycle events (ModSystem fields, statics, caches); in-memory map chunk state (height
    /// maps, map moddata), which the engine keeps preferring over the restored blobs; and, for
    /// players, animation/interaction state (test players are headless) and privileges/roles
    /// (host-scoped, not world state). Scenarios sensitive to those need
    /// <see cref="FreshWorld"/>.</para>
    /// <para>The mod cooperation contract (stage 3): a mod whose in-memory state is keyed to
    /// SaveGame data (a registry seeded from a persisted manifest, an id allocator, generated
    /// markers) desyncs from the restored SaveGame on rollback unless it participates. Atlas
    /// pushes two engine event-bus events, synchronously on the game thread:
    /// <c>atlas:rollback:captured</c> (once per capture, right after the snapshot is in memory)
    /// and <c>atlas:rollback:restored</c> (every restore, AFTER the database and SaveGame
    /// globals are restored and BEFORE any chunk column reloads). Payloads are versioned
    /// <c>TreeAttribute</c>s: <c>version</c> (int, 1), <c>generation</c> (int, per capture),
    /// plus <c>restoreCount</c> (int) on the restored event. A cooperating mod subscribes with
    /// <c>api.Event.RegisterEventBusListener(handler, priority, "atlas:rollback:restored")</c>
    /// and rebuilds its in-memory state from <c>api.WorldManager.SaveGame</c>, exactly as at
    /// boot: the mod references only VintagestoryAPI, never an Atlas assembly, and the listener
    /// is inert outside Atlas runs (the events never fire in production). Library mods that
    /// other mods build on should subscribe above the 0.5 default priority. Mods that follow
    /// chunk/entity lifecycle events need nothing; mods with neither lifecycle-keyed state nor
    /// a hook need <see cref="FreshWorld"/>. A throwing handler degrades the rollback
    /// fail-closed ("mod rollback hook failed") and fails the scenario under
    /// <see cref="StrictIsolation"/>.</para>
    /// <para>Fail closed: if capture or restore fails for any reason (including engine drift in
    /// a future game version), Atlas logs a one-line warning to stderr and falls back to the
    /// <see cref="FreshWorld"/> full-recycle path, so the scenario still gets its clean world.
    /// Every degrade is also attached to the scenario's own test output (visible in the IDE test
    /// explorer, the TRX report and `atlas run`), with the reason and the fallback recycle cost,
    /// and each class gets an end-of-class isolation summary on stderr; scenarios that treat the
    /// speedup as a contract can fail instead via <see cref="StrictIsolation"/>. Combining
    /// <see cref="RollbackWorld"/> with <see cref="FreshWorld"/> or <see cref="RestartWorld"/>
    /// is a setup error: the three world modes contradict pairwise.</para></remarks>
    public bool RollbackWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether the class host is genuinely RESTARTED
    /// before this scenario runs: the current host is shut down gracefully (the engine's
    /// shutdown persists the world save), and a replacement host boots against that persisted
    /// save. The scenario then runs on a truly restarted server whose world carried over, so it
    /// can assert on what actually survives a save/load round trip: <c>SaveGame.ModData</c>,
    /// manifests, whatever a mod writes for reload. <see cref="FreshWorld"/> throws the world
    /// away and <see cref="RollbackWorld"/> restores state without restarting the server;
    /// neither exercises the persistence path this mode is for.</summary>
    /// <remarks><para>Cost: one graceful shutdown plus one full boot, the same order of
    /// magnitude as <see cref="FreshWorld"/>. That is the point, not a defect: the boot IS the
    /// save/load round trip under test. If this scenario is the first of its class (or the class
    /// does not own the live host yet), the class host is booted first and then restarted, so
    /// even a first scenario gets a genuine round trip; that case costs two boots.</para>
    /// <para>Composition with a class-level <c>[AtlasWorld(SaveFile = ...)]</c>: the restart
    /// carries forward the CURRENT world state, mutations made by earlier scenarios included,
    /// not the original fixture. A scenario that needs the pristine fixture back should use
    /// <see cref="FreshWorld"/> instead.</para>
    /// <para>Fail hard, never fall back: a failed harvest (no persisted save after the graceful
    /// shutdown) fails the scenario with an <c>AtlasSetupException</c>, and a crash while
    /// booting the replacement surfaces as-is. There is no silent degrade, which is also why
    /// combining this with <see cref="StrictIsolation"/> is a setup error (nothing can degrade,
    /// so there is nothing to be strict about). Combining it with <see cref="FreshWorld"/> or
    /// <see cref="RollbackWorld"/> is a setup error too: the three modes contradict pairwise.</para>
    /// <para>Joined test players do NOT survive a restart: their connections die with the host.
    /// Requesting a restart on a class that has joined test players fails the scenario with an
    /// <c>AtlasSetupException</c> rather than silently dropping them; re-join players after the
    /// restart, use <see cref="RollbackWorld"/> (player-aware since stage 2) when a restored
    /// world is enough, or use <see cref="FreshWorld"/> when the carried-over world is not
    /// actually needed.</para></remarks>
    public bool RestartWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether a degraded <see cref="RollbackWorld"/>
    /// request FAILS the scenario instead of silently falling back to a full host recycle:
    /// opt-in for suites that treat the rollback speedup as a contract. The failure is an
    /// <c>AtlasIsolationException</c> carrying the degrade reason (a mod rollback hook
    /// failure, engine drift, or a generic capture/restore failure).</summary>
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
