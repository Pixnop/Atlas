using Atlas.Api;

namespace Atlas.Internal.Staging;

/// <summary>Copies mods-under-test (folder, zip or dll) into the staging folder the server loads from.</summary>
internal static class ModStager
{
    // Names compare the way the file system does, so "Mod.dll" and "mod.dll" collide on Windows
    // and macOS, where they are one file, and stay two mods on Linux.
    private static readonly StringComparer NameComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Resolves and stages mod paths into the staging directory.</summary>
    /// <param name="modPaths">Relative or absolute paths to mod files or directories.</param>
    /// <param name="baseDir">Base directory for resolving relative paths.</param>
    /// <param name="stagingDir">Target staging directory.</param>
    /// <returns>Where each staged mod came from: the full path of its copy in the staging
    /// directory, mapped to the full path it was copied from (what the caller gave, resolved
    /// against <paramref name="baseDir"/>). The same path given twice is staged once, and so is
    /// one entry.</returns>
    /// <exception cref="AtlasSetupException">Thrown when one or more mod paths do not exist, or
    /// when two different mods would be staged under the same name (a file or folder name is the
    /// staging name, so two mod folders both called <c>net10.0</c> would be merged into one).
    /// Nothing is copied in either case.</exception>
    public static IReadOnlyDictionary<string, string> Stage(IReadOnlyList<string> modPaths, string baseDir, string stagingDir)
    {
        ArgumentNullException.ThrowIfNull(modPaths);
        var missing = new List<string>();
        var resolved = new List<string>();
        foreach (string path in modPaths)
        {
            string full = Path.GetFullPath(path, baseDir);
            if (File.Exists(full) || Directory.Exists(full))
            {
                resolved.Add(full);
            }
            else
            {
                missing.Add(path);
            }
        }

        if (missing.Count > 0)
        {
            throw new AtlasSetupException("Mod path(s) not found: " + string.Join(", ", missing));
        }

        // Name every source first and refuse a collision before copying anything: a staging name
        // is the file or folder name, so two different mod folders both called "net10.0" (two
        // projects' build outputs) would otherwise be copied over each other into one folder.
        var named = new List<(string Source, string Name)>();
        var firstWithName = new Dictionary<string, string>(NameComparer);
        foreach (string source in resolved)
        {
            // Trim trailing directory separators before deriving the staging name: a path like
            // "C:\out\mymod\" (e.g. produced by MSBuild's %(RootDir)%(Directory)) makes
            // Path.GetFileName return "", which would otherwise silently flatten the folder's
            // contents into the staging root instead of nesting them under a mod folder.
            string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new AtlasSetupException(
                    $"Could not derive a staging folder name from mod path '{source}': " +
                    "the path has no file/directory name component after trimming trailing separators.");
            }

            if (firstWithName.TryGetValue(name, out string? other))
            {
                if (NameComparer.Equals(other, trimmed))
                {
                    continue;
                }

                throw new AtlasSetupException(
                    $"Two mods would be staged under the same name '{name}': '{other}' and '{trimmed}'. " +
                    "Atlas stages each mod under its file or folder name, so the second would be merged into " +
                    "the first and the game would refuse both. Give each mod its own folder name (one folder " +
                    "per mod, named after it, rather than two build outputs both called " +
                    $"'{name}'; a ProjectReference tagged <AtlasMod>true</AtlasMod> is staged that way for you), " +
                    "or rename one of the files.");
            }

            firstWithName[name] = trimmed;
            named.Add((trimmed, name));
        }

        Directory.CreateDirectory(stagingDir);
        var sources = new Dictionary<string, string>();
        foreach ((string trimmed, string name) in named)
        {
            string staged = Path.Combine(stagingDir, name);
            if (File.Exists(trimmed))
            {
                File.Copy(trimmed, staged, overwrite: true);
            }
            else
            {
                CopyTree(new DirectoryInfo(trimmed), staged);
            }

            sources[Path.GetFullPath(staged)] = trimmed;
        }

        return sources;
    }

    /// <summary>Stages the bridge assembly, alone, into its own staging folder.</summary>
    /// <param name="bridgeSource">Full path of the bridge assembly to copy.</param>
    /// <param name="stagingDir">Target staging directory, created if missing.</param>
    /// <exception cref="AtlasSetupException">Thrown when the copy fails, so a broken bridge
    /// staging reads as a setup failure instead of an opaque host crash.</exception>
    public static void StageBridge(string bridgeSource, string stagingDir)
    {
        string trimmedSource = bridgeSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string bridgeName = Path.GetFileName(trimmedSource);
        if (string.IsNullOrWhiteSpace(bridgeName))
        {
            throw new AtlasSetupException(
                $"Could not derive a staging file name from bridge path '{bridgeSource}': " +
                "the path has no file name component after trimming trailing separators.");
        }

        string destination = Path.Combine(stagingDir, bridgeName);
        try
        {
            Directory.CreateDirectory(stagingDir);
            File.Copy(bridgeSource, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new AtlasSetupException(
                $"Failed to stage the Atlas bridge mod: could not copy '{bridgeSource}' " +
                $"to '{destination}'. See the inner exception for the file system error.",
                ex);
        }
    }

    /// <summary>Recursively copies a directory's contents into <paramref name="to"/>, creating it
    /// if needed and overwriting files that already exist. Shared with <see cref="DataSeeder"/>.</summary>
    /// <param name="from">The directory whose contents are copied.</param>
    /// <param name="to">The target directory.</param>
    /// <param name="copyFile">Copies one file to its target path, for a caller that has to treat
    /// file contents (the data seeding resolves tokens); <see langword="null"/> copies the file
    /// as it is.</param>
    internal static void CopyTree(DirectoryInfo from, string to, Action<FileInfo, string>? copyFile = null)
    {
        Directory.CreateDirectory(to);
        foreach (FileInfo file in from.GetFiles())
        {
            string target = Path.Combine(to, file.Name);
            if (copyFile is null)
            {
                file.CopyTo(target, overwrite: true);
            }
            else
            {
                copyFile(file, target);
            }
        }

        foreach (DirectoryInfo dir in from.GetDirectories())
        {
            CopyTree(dir, Path.Combine(to, dir.Name), copyFile);
        }
    }
}
