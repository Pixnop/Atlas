using System.Runtime.CompilerServices;
using Atlas.XUnit.Internal;

namespace Atlas.Cli;

/// <summary>Bridges worker mode to the harness's isolation summary sink,
/// <see cref="IsolationSummarySink.Install"/>: once installed, every per-class isolation summary
/// the harness prints to stderr at a host hand-off is also delivered to the worker, which turns
/// it into a <c>class-summary</c> protocol event. The CLI ships no harness copy of its own: it
/// compiles against <c>Atlas.XUnit</c> and installs into the copy the scenario assembly ships,
/// which <see cref="ScenarioAssemblyResolver"/> loads on the spot (registration happens before
/// the run starts, so this call is usually what brings the harness in). A harness too old to
/// carry the sink gets <see cref="HarnessSeam"/>'s diagnostic, like the other two seams.</summary>
internal static class IsolationSummaryHook
{
    /// <summary>Registers <paramref name="handler"/> as the harness's isolation summary sink.
    /// Dispose the registration to uninstall the handler.</summary>
    /// <param name="handler">Receives the class's fully qualified name and the formatted
    /// summary line, on whatever thread the harness hands the host off.</param>
    /// <param name="error">A diagnostic when the loaded harness predates the sink; null on
    /// success.</param>
    /// <returns>The registration to dispose when the run is over, or null with
    /// <paramref name="error"/> set.</returns>
    public static IDisposable? Register(Action<string, string> handler, out string? error)
    {
        ArgumentNullException.ThrowIfNull(handler);
        error = HarnessSeam.TryCall(HarnessSeam.AdapterAssemblyName, () => Install(handler));
        return error is null ? new Registration() : null;
    }

    /// <summary>The seam itself: the only method naming the harness, so its assembly loads no
    /// earlier than the first call, by which point <see cref="ScenarioAssemblyResolver"/> can
    /// answer for it. Kept out of the caller's JIT deliberately (the pattern
    /// <see cref="StageRunner"/> follows for the staging seam).</summary>
    /// <param name="handler">The handler to install, or null to uninstall.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Install(Action<string, string>? handler) => IsolationSummarySink.Install(handler);

    /// <summary>One live registration: uninstalls the handler on dispose. Uninstalling twice is
    /// the same no-op as uninstalling once, so no disposed flag is needed.</summary>
    private sealed class Registration : IDisposable
    {
        public void Dispose() => Install(null);
    }
}
