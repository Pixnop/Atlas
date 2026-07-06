using System.Reflection;

namespace Atlas.Cli;

/// <summary>Resolves assembly load requests against the scenario assembly's directory for the
/// lifetime of a run. The CLI process starts with only its own dependencies on the probing path;
/// the scenario assembly's dependencies (Atlas, Atlas.XUnit, xunit.execution, VintagestoryAPI,
/// whatever the scenarios reference) all sit next to the scenario dll, exactly where `dotnet
/// test` would have found them, so a plain directory probe is sufficient. The engine installs
/// its own resolve hook for the game install once a server boots; this one only bridges the gap
/// between the CLI's output directory and the scenario assembly's.</summary>
internal sealed class ScenarioAssemblyResolver : IDisposable
{
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="ScenarioAssemblyResolver"/> class
    /// and installs the resolve hook.</summary>
    /// <param name="directory">The scenario assembly's directory.</param>
    public ScenarioAssemblyResolver(string directory)
    {
        _directory = directory;
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    /// <summary>Uninstalls the resolve hook.</summary>
    public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

    /// <summary>Maps an assembly full name to the candidate dll path inside a directory. Pure
    /// (the probe is injected), so the mapping is unit-testable without loading anything.</summary>
    /// <param name="assemblyFullName">The requested assembly's full (or simple) name.</param>
    /// <param name="directory">The directory to probe.</param>
    /// <param name="fileExists">File-existence probe, injectable for tests.</param>
    /// <returns>The dll path when the directory holds a candidate; null otherwise.</returns>
    internal static string? ResolvePath(string assemblyFullName, string directory, Func<string, bool> fileExists)
    {
        string? simpleName = new AssemblyName(assemblyFullName).Name;
        if (string.IsNullOrEmpty(simpleName))
        {
            return null;
        }

        string candidate = Path.Combine(directory, simpleName + ".dll");
        return fileExists(candidate) ? candidate : null;
    }

    private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string? path = ResolvePath(args.Name, _directory, File.Exists);
        return path is null ? null : Assembly.LoadFrom(path);
    }
}
