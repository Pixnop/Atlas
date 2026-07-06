using Atlas.Api;

namespace Atlas.Internal.Staging;

/// <summary>Copies mods-under-test (folder, zip or dll) into the staging folder the server loads from.</summary>
internal static class ModStager
{
    /// <summary>Resolves and stages mod paths into the staging directory.</summary>
    /// <param name="modPaths">Relative or absolute paths to mod files or directories.</param>
    /// <param name="baseDir">Base directory for resolving relative paths.</param>
    /// <param name="stagingDir">Target staging directory.</param>
    /// <exception cref="AtlasSetupException">Thrown when one or more mod paths do not exist.</exception>
    public static void Stage(IReadOnlyList<string> modPaths, string baseDir, string stagingDir)
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

        Directory.CreateDirectory(stagingDir);
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

            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(stagingDir, name), overwrite: true);
            }
            else
            {
                CopyTree(new DirectoryInfo(trimmed), Path.Combine(stagingDir, name));
            }
        }
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
    internal static void CopyTree(DirectoryInfo from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (FileInfo file in from.GetFiles())
        {
            file.CopyTo(Path.Combine(to, file.Name), overwrite: true);
        }

        foreach (DirectoryInfo dir in from.GetDirectories())
        {
            CopyTree(dir, Path.Combine(to, dir.Name));
        }
    }
}
