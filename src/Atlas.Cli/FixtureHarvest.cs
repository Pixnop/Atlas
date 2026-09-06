using System.Runtime.CompilerServices;
using Atlas.XUnit.Internal;

namespace Atlas.Cli;

/// <summary>Bridges `atlas fixture` to the harness's harvest seam,
/// <see cref="HostRegistry.ShutDownAndHarvestSavePathAsync"/>: dispose the builder scenario's
/// host gracefully (the engine's shutdown persists the world save) and return the save's path
/// inside the host's scratch data path. The CLI ships no harness copy of its own: it compiles
/// against <c>Atlas.XUnit</c> and executes the copy the scenario assembly ships, which
/// <see cref="ScenarioAssemblyResolver"/> loaded (see Atlas.Cli.csproj), so a harness too old to
/// carry the seam gets <see cref="HarnessSeam"/>'s diagnostic instead of a raw crash.</summary>
internal static class FixtureHarvest
{
    /// <summary>Shuts the current scenario host down gracefully and returns its world save path.</summary>
    /// <param name="error">A diagnostic when the loaded harness predates the seam; null on
    /// success (including the no-host case).</param>
    /// <returns>The save file path, or null when no host was live or the seam was missing.</returns>
    public static string? ShutDownAndHarvestSavePath(out string? error)
    {
        string? savePath = null;
        error = HarnessSeam.TryCall(HarnessSeam.AdapterAssemblyName, () => savePath = Harvest());
        return savePath;
    }

    /// <summary>The seam itself: the only method naming the harness, so its assembly loads no
    /// earlier than the first call, by which point the scenario run has long since brought it in.
    /// Kept out of the caller's JIT deliberately (the pattern <see cref="StageRunner"/> follows
    /// for the staging seam).</summary>
    /// <returns>The disposed host's save file path, or null when no host was live.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Harvest() =>
        HostRegistry.ShutDownAndHarvestSavePathAsync().GetAwaiter().GetResult();
}
