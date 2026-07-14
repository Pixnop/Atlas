using System.Reflection;

namespace Atlas.Internal.Hosting;

/// <summary>The pure core of the entity-simulation tick counter behind
/// <see cref="Api.IWorldSession.EntitySimulationTicks"/>: which engine system carries the
/// signal (<see cref="FindEntitySimulationSystem"/>), which field records its last tick
/// (<see cref="ResolveStampField"/>), when a sampled stamp means a new tick
/// (<see cref="HasTicked"/>), and how the drift failure is worded
/// (<see cref="DescribeUnavailable"/>) - all testable without booting a server (the
/// <see cref="AssetsBuildSignal"/> pattern). The live reads against a running
/// <c>ServerMain</c> stay in the thin shell, <see cref="EntitySimulationTickCounter"/>.</summary>
/// <remarks>The engine flow being counted (verified by decompile, identical on 1.20.12,
/// 1.21.7 and 1.22.3, untouched by the Stratum fork's patches): every
/// <c>ServerMain.Process()</c> pass ticks each entry of the internal
/// <c>ServerMain.Systems</c> array at most once, when the master clock has advanced past the
/// system's own stride (<c>elapsed - system.millisecondsSinceStart &gt;
/// GetUpdateInterval()</c>, 20 ms for entity simulation), and stamps
/// <c>millisecondsSinceStart</c> with the pass's clock snapshot on fire. Because a fire
/// requires the clock to have advanced past the stride, consecutive stamps are strictly
/// increasing: sampling the stamp once per pump pass observes every fire exactly once. See
/// docs/specs/2026-07-14-tick-contract.md for the full contract.</remarks>
internal static class SimulationTickSignal
{
    /// <summary>The engine type that owns entity ticking, on every supported version:
    /// <c>Vintagestory.Server.ServerSystemEntitySimulation</c>, whose <c>OnServerTick</c>
    /// calls <c>Entity.OnGameTick</c> on all loaded entities (<c>TickEntities</c>).</summary>
    internal const string EntitySimulationTypeName = "ServerSystemEntitySimulation";

    /// <summary>The <c>ServerSystem</c> base field the engine stamps with the master-clock
    /// snapshot each time the system ticks; public on every supported version.</summary>
    internal const string StampFieldName = "millisecondsSinceStart";

    /// <summary>Picks the entity-simulation system out of the engine's systems array: the
    /// first entry whose type (or a base type, for forks that subclass instead of patching)
    /// is named <see cref="EntitySimulationTypeName"/>.</summary>
    /// <param name="systems">The engine's <c>ServerMain.Systems</c> array, as read
    /// reflectively; <see langword="null"/> when the field itself drifted away.</param>
    /// <returns>The system instance, or <see langword="null"/> when the array is missing or
    /// no entry matches (engine layout drift); callers degrade the counter.</returns>
    public static object? FindEntitySimulationSystem(Array? systems)
    {
        if (systems == null)
        {
            return null;
        }

        foreach (object? system in systems)
        {
            for (Type? type = system?.GetType(); type != null; type = type.BaseType)
            {
                if (type.Name == EntitySimulationTypeName)
                {
                    return system;
                }
            }
        }

        return null;
    }

    /// <summary>Resolves the tick-stamp field on the entity-simulation system's type:
    /// the public <see cref="long"/> <see cref="StampFieldName"/> declared on the
    /// <c>ServerSystem</c> base (which <see cref="Type.GetField(string, BindingFlags)"/>
    /// returns for a public inherited field). Type-parameterized so the resolution rule is
    /// testable against fake system shapes without booting a server.</summary>
    /// <param name="systemType">The runtime type of the entity-simulation system.</param>
    /// <returns>The resolved field, or <see langword="null"/> when it is missing or no
    /// longer a <see cref="long"/> (engine layout drift); callers degrade the counter.</returns>
    public static FieldInfo? ResolveStampField(Type systemType)
    {
        ArgumentNullException.ThrowIfNull(systemType);
        FieldInfo? field = systemType.GetField(StampFieldName, BindingFlags.Public | BindingFlags.Instance);
        return field?.FieldType == typeof(long) ? field : null;
    }

    /// <summary>Decides whether a sampled stamp means the system ticked since the previous
    /// sample. A fire always moves the stamp (the fire condition requires the clock to have
    /// advanced past the stride), and the system fires at most once per engine pass, so
    /// per-pass sampling makes "stamp changed" equivalent to "exactly one tick ran".</summary>
    /// <param name="lastStamp">The stamp observed by the previous sample.</param>
    /// <param name="currentStamp">The stamp observed by this sample.</param>
    /// <returns>Whether the system ticked between the two samples.</returns>
    public static bool HasTicked(long lastStamp, long currentStamp) => currentStamp != lastStamp;

    /// <summary>Words the failure of reading <see cref="Api.IWorldSession.EntitySimulationTicks"/>
    /// on an engine whose tick machinery drifted, naming the exact symbols so the drift can be
    /// re-measured (the <see cref="ServerAssetsBuildProbe"/> warning voice).</summary>
    /// <param name="gameVersion">The loaded game version.</param>
    /// <returns>The exception message.</returns>
    public static string DescribeUnavailable(string gameVersion)
        => "EntitySimulationTicks is unavailable: the engine's entity-simulation tick signal " +
           $"(the internal 'ServerMain.Systems' array, a '{EntitySimulationTypeName}' entry, " +
           $"and its public 'long {StampFieldName}' stamp) was not found on game version " +
           $"{gameVersion}. The engine layout drifted and Atlas cannot count real simulation " +
           "ticks; scenarios that do not read this property are unaffected.";
}
