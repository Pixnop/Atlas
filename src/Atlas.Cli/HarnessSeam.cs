using System.Reflection;

namespace Atlas.Cli;

/// <summary>The one guard behind every call the CLI makes into the harness. Three commands need
/// something from inside it (`atlas stage` runs the staging decision, `atlas fixture` harvests
/// the builder's world save, worker mode installs the isolation-summary sink), and all three
/// compile against the repo's own Atlas/Atlas.XUnit while executing whatever copy the target
/// directory ships (<see cref="ScenarioAssemblyResolver"/>, <see cref="StageAssemblyResolver"/>).
/// That gap is the point: the assembly's harness version is the one that runs. When the two
/// disagree about a member, the runtime says so with a raw <see cref="MissingMethodException"/>
/// or friend, which reads as a crash rather than as version skew; this class turns it into the
/// diagnostic naming both versions.</summary>
/// <remarks>Two rules make a seam safe, and both are the caller's job. The call must sit in its
/// own method, invoked only once the resolver is installed: JIT-compiling a method resolves every
/// type it names, so a harness type mentioned inline in a method that runs before the resolver
/// would try to load the harness too early. And the call must go through
/// <see cref="TryCall"/>, so the mismatch surfaces as this diagnostic on every seam.</remarks>
internal static class HarnessSeam
{
    /// <summary>Simple name of the harness adapter assembly, which owns the fixture-harvest and
    /// isolation-summary seams.</summary>
    internal const string AdapterAssemblyName = "Atlas.XUnit";

    /// <summary>Simple name of the harness engine assembly, which owns the staging seam.</summary>
    internal const string EngineAssemblyName = "Atlas";

    /// <summary>Runs one seam call against the loaded harness.</summary>
    /// <param name="assemblyName">Simple name of the harness assembly the call lands in, named
    /// in the diagnostic: <see cref="AdapterAssemblyName"/> or <see cref="EngineAssemblyName"/>.</param>
    /// <param name="call">The call, in its own method so the harness loads no earlier than this
    /// invocation.</param>
    /// <returns><see langword="null"/> when the call ran; the diagnostic when the loaded harness
    /// does not carry the member this CLI was compiled against.</returns>
    public static string? TryCall(string assemblyName, Action call)
    {
        try
        {
            call();
            return null;
        }
        catch (Exception exception) when (
            exception is MissingMethodException or MissingFieldException
                or TypeLoadException or MethodAccessException)
        {
            return VersionMismatch(assemblyName);
        }
    }

    /// <summary>Formats the version-skew diagnostic for a harness assembly.</summary>
    /// <param name="assemblyName">Simple name of the harness assembly that lacked the member.</param>
    /// <returns>A message naming the loaded harness version and this CLI's own.</returns>
    internal static string VersionMismatch(string assemblyName) =>
        $"{assemblyName}.dll is v{LoadedVersion(assemblyName)} and this atlas CLI is "
        + $"v{CliVersion.Resolve()}: update the tool or rebuild the test project.";

    /// <summary>Reads the informational version of a harness assembly already loaded in this
    /// process. Deliberately searched by name rather than through <c>typeof(...).Assembly</c>,
    /// which would itself throw on the very targets this diagnostic exists for.</summary>
    /// <param name="assemblyName">Simple name of the harness assembly.</param>
    /// <returns>The version, or <see cref="CliVersion.Unknown"/> when the assembly is not loaded
    /// or carries no informational version.</returns>
    private static string LoadedVersion(string assemblyName) => CliVersion.FromInformationalVersion(
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
