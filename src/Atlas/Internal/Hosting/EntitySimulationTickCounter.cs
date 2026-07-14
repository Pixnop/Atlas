using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Atlas.Internal.Bootstrap;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Counts the embedded server's real entity-simulation ticks, the engine-side truth
/// behind <see cref="Api.IWorldSession.EntitySimulationTicks"/>: a thin reflective shell over
/// the engine's own record of the entity-simulation system's last tick
/// (<c>ServerSystem.millisecondsSinceStart</c>, stamped by <c>ServerMain.Process()</c> on
/// every fire), sampled by the game-thread pump after each <c>Process()</c> pass.</summary>
/// <remarks><para>Why sampling is exact: the engine ticks each server system at most once per
/// <c>Process()</c> pass, and a fire strictly increases the stamp (the fire condition requires
/// the master clock to have advanced past the system's 20 ms stride), so one sample per pass
/// observes every fire exactly once - no fire can hide between samples, none can be counted
/// twice. The pump samples from boot onward, so the count covers the host's whole life.
/// Symbol shapes verified by decompile on 1.20.12, 1.21.7 and 1.22.3 (and untouched by the
/// Stratum fork): <c>internal ServerSystem[] ServerMain.Systems</c>, built by <c>Launch()</c>
/// before the first <c>Process()</c> call; <c>public class ServerSystemEntitySimulation</c>;
/// <c>public long millisecondsSinceStart</c>. See docs/specs/2026-07-14-tick-contract.md.</para>
/// <para>The <see cref="FieldInfo"/>s are cached process-wide (the engine's layout is a
/// per-game-version fact; hosts recycle many times per suite). On a drifted engine
/// <see cref="TryCreate"/> returns <see langword="null"/>: the host boots normally, warns once
/// (<see cref="WarnCounterMissingOnce"/>), and only a scenario that actually reads the counter
/// fails, with the drifted symbols named (<see cref="SimulationTickSignal.DescribeUnavailable"/>).
/// The resolution rules and the tick decision live in the pure
/// <see cref="SimulationTickSignal"/>; this shell only holds the live system reference and the
/// running count.</para></remarks>
internal sealed class EntitySimulationTickCounter
{
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Reads the engine's internal Systems array to reach the entity-simulation system; a missing field (engine layout drift) degrades to an unavailable counter with a one-time warning instead of failing the boot.")]
    private static readonly Lazy<FieldInfo?> SystemsField = new(() => typeof(ServerMain).GetField(
        "Systems", BindingFlags.NonPublic | BindingFlags.Instance));

    /// <summary>One-time latch for <see cref="WarnCounterMissingOnce"/>.</summary>
    private static int counterMissingWarned;

    /// <summary>Cached stamp field, resolved once per process (every host's entity-simulation
    /// system has the same runtime type). Stays <see langword="null"/> on a drifted layout.</summary>
    private static FieldInfo? stampField;

    private readonly object _system;
    private readonly FieldInfo _stampField;
    private long _lastStamp;
    private long _count;

    private EntitySimulationTickCounter(object system, FieldInfo field, long baselineStamp)
    {
        _system = system;
        _stampField = field;
        _lastStamp = baselineStamp;
    }

    /// <summary>Gets the number of entity-simulation ticks sampled so far.</summary>
    /// <remarks>Written on the game thread by <see cref="Sample"/>; read through
    /// <see cref="Volatile"/> so a cross-thread reader (e.g. future diagnostics) never observes
    /// a torn value, mirroring <see cref="Scheduling.TickSource.TickCount"/>. Scenario reads run
    /// on the game thread and always see the current count.</remarks>
    public long Count => Volatile.Read(ref _count);

    /// <summary>Creates a counter over <paramref name="server"/>'s entity-simulation system, or
    /// returns <see langword="null"/> when the engine layout drifted (the systems array is gone,
    /// no entity-simulation system is in it, or its stamp field changed shape); callers degrade
    /// to an unavailable counter. Must run after <c>Launch()</c> (which builds the systems
    /// array) and before the first <c>Process()</c> call, so no tick predates the baseline.</summary>
    /// <param name="server">The launched, not yet pumped embedded server.</param>
    /// <returns>The counter, or <see langword="null"/> on engine layout drift.</returns>
    public static EntitySimulationTickCounter? TryCreate(ServerMain server)
    {
        object? system = SimulationTickSignal.FindEntitySimulationSystem(
            (Array?)SystemsField.Value?.GetValue(server));
        if (system == null)
        {
            return null;
        }

        stampField ??= SimulationTickSignal.ResolveStampField(system.GetType());
        return stampField == null
            ? null
            : new EntitySimulationTickCounter(system, stampField, (long)stampField.GetValue(system)!);
    }

    /// <summary>Samples the engine's tick stamp and advances the count when the
    /// entity-simulation system ticked since the previous sample. Called by the game-thread
    /// pump once after every <c>server.Process()</c> pass, before the scheduler drains, so a
    /// scenario continuation resumed on this pass already sees the pass's tick counted.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public void Sample()
    {
        long stamp = (long)_stampField.GetValue(_system)!;
        if (SimulationTickSignal.HasTicked(_lastStamp, stamp))
        {
            _lastStamp = stamp;
            Volatile.Write(ref _count, _count + 1);
        }
    }

    /// <summary>Logs the engine-layout-drift warning once per process: hosts recycle many times
    /// per suite and the drift is a per-game-version fact, not a per-boot one.</summary>
    public static void WarnCounterMissingOnce()
    {
        if (Interlocked.Exchange(ref counterMissingWarned, 1) == 0)
        {
            Console.Error.WriteLine(
                "[Atlas] " + SimulationTickSignal.DescribeUnavailable(EngineCompat.ShortGameVersion));
        }
    }
}
