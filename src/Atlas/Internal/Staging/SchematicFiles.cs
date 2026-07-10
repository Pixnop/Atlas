using Atlas.Api;

namespace Atlas.Internal.Staging;

/// <summary>Path resolution and failure reporting for schematic files placed through
/// <see cref="IWorldSession"/>.</summary>
internal static class SchematicFiles
{
    /// <summary>Resolves a schematic path: an absolute path is taken as-is, a relative path
    /// resolves against <paramref name="baseDir"/>, the same base directory mod paths and
    /// <see cref="WorldOptions.SaveFile"/> resolve against.</summary>
    /// <param name="path">Relative or absolute path to the schematic file.</param>
    /// <param name="baseDir">Base directory for resolving a relative path.</param>
    /// <returns>The absolute path to load.</returns>
    public static string Resolve(string path, string baseDir) => Path.GetFullPath(path, baseDir);

    /// <summary>Builds the message for a schematic the engine could not load.</summary>
    /// <param name="path">The path as the caller gave it.</param>
    /// <param name="resolvedPath">The absolute path the engine was asked to load.</param>
    /// <param name="engineError">The engine's error string, possibly empty: the engine's loader
    /// returns no schematic and no error for a JSON body that deserializes to nothing (e.g. a
    /// file containing just <c>null</c>).</param>
    /// <returns>The message naming the given path (plus the resolved path when they differ) and
    /// the reason the load failed.</returns>
    public static string LoadFailureMessage(string path, string resolvedPath, string engineError)
    {
        string location = path == resolvedPath
            ? $"'{path}'"
            : $"'{path}' (resolved to '{resolvedPath}')";
        string reason = string.IsNullOrEmpty(engineError)
            ? "the engine returned no schematic and no error; is the file a schematic JSON export?"
            : engineError;
        return $"Failed to load schematic {location}: {reason}";
    }
}
