namespace Atlas.Cli;

/// <summary>Resolves the `atlas stage` target argument to the directory the staging decision
/// operates on. Pure string manipulation (mirrors <see cref="ScenarioAssemblyResolver.ResolvePath"/>'s
/// injectable style): no file-system check here, so a nonexistent path resolves exactly like a
/// valid one would; <see cref="StageRunner"/> validates existence afterward.</summary>
internal static class StagePathResolution
{
    /// <summary>Resolves the target argument: a path ending in .dll (any casing) means its
    /// containing directory (the test output that ships next to the compiled assembly);
    /// anything else is taken as the directory itself.</summary>
    /// <param name="targetPath">The raw positional argument, as the user gave it.</param>
    /// <returns>The resolved directory path, not guaranteed to exist.</returns>
    internal static string ResolveTargetDirectory(string targetPath)
    {
        string trimmed = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
        {
            // A path that was all separators (e.g. "/"): TrimEnd emptied it, so fall back to the
            // original rather than resolving to the current directory by surprise.
            return targetPath;
        }

        if (!string.Equals(Path.GetExtension(trimmed), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Path.GetDirectoryName(trimmed) is { Length: > 0 } directory ? directory : ".";
    }
}
