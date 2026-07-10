using System.Reflection;

namespace Atlas.Cli;

/// <summary>Bridges `atlas fixture` to the harness's harvest seam,
/// <c>Atlas.XUnit.Internal.HostRegistry.ShutDownAndHarvestSavePathAsync</c>: dispose the builder
/// scenario's host gracefully (the engine's shutdown persists the world save) and return the
/// save's path inside the host's scratch data path. The CLI deliberately references neither
/// Atlas nor Atlas.XUnit (the scenario assembly ships the whole harness, see Atlas.Cli.csproj),
/// so the seam is invoked BY NAME through reflection against the Atlas.XUnit instance the
/// scenario run loaded; the names below are the contract both sides pin.</summary>
internal static class FixtureHarvest
{
    /// <summary>Simple name of the harness assembly holding the harvest seam.</summary>
    internal const string AdapterAssemblyName = "Atlas.XUnit";

    /// <summary>Fully qualified name of the host registry type owning the live host.</summary>
    internal const string RegistryTypeName = "Atlas.XUnit.Internal.HostRegistry";

    /// <summary>Name of the harvest method (static, parameterless, returns Task of string).</summary>
    internal const string HarvestMethodName = "ShutDownAndHarvestSavePathAsync";

    /// <summary>Shuts the current scenario host down gracefully and returns its world save path.</summary>
    /// <param name="error">A diagnostic when the seam could not be found or invoked; null on
    /// success (including the no-host case).</param>
    /// <returns>The save file path, or null when no host was live or the seam was missing.</returns>
    public static string? ShutDownAndHarvestSavePath(out string? error)
    {
        MethodInfo? harvest = FindHarvestMethod(
            AppDomain.CurrentDomain.GetAssemblies(), out error);
        if (harvest is null)
        {
            return null;
        }

        var pending = (Task<string?>)harvest.Invoke(null, null)!;
        return pending.GetAwaiter().GetResult();
    }

    /// <summary>Locates the harvest method on the loaded harness, if any. Separated from the
    /// invocation so the lookup and its error texts are testable against arbitrary assembly
    /// lists.</summary>
    /// <param name="loadedAssemblies">The assemblies currently loaded in the process.</param>
    /// <param name="error">A diagnostic when the harness or the seam is missing; null on success.</param>
    /// <returns>The harvest method, or null with <paramref name="error"/> set.</returns>
    internal static MethodInfo? FindHarvestMethod(IEnumerable<Assembly> loadedAssemblies, out string? error)
    {
        Assembly? adapter = loadedAssemblies.FirstOrDefault(
            assembly => assembly.GetName().Name == AdapterAssemblyName);
        if (adapter is null)
        {
            error = $"the scenario run never loaded {AdapterAssemblyName}; is the assembly an Atlas scenario assembly?";
            return null;
        }

        MethodInfo? harvest = adapter
            .GetType(RegistryTypeName)?
            .GetMethod(HarvestMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (harvest is null)
        {
            error = $"the scenario assembly's {AdapterAssemblyName} does not expose "
                + $"{RegistryTypeName}.{HarvestMethodName}; rebuild it against Atlas 0.7 or newer.";
            return null;
        }

        error = null;
        return harvest;
    }
}
