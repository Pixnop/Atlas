using Atlas.Api;

namespace Atlas.Internal.Staging;

/// <summary>Copies declared data files (<see cref="DataFileSeed"/>) into the scratch data path
/// before the embedded server boots, so mods that read configuration during startup (e.g.
/// <c>api.LoadModConfig</c> in <c>StartServerSide</c>) see them.</summary>
internal static class DataSeeder
{
    /// <summary>File name the embedded server's save location is pinned to (see
    /// <c>ServerHost.BootServer</c>). A seeded world save is copied under this name so the
    /// fixture's own file name does not matter.</summary>
    internal const string WorldSaveFileName = "atlas.vcdbs";

    /// <summary>Copies a prebuilt world save into the data path, under the pinned save name, so
    /// the engine loads it instead of generating a fresh world.</summary>
    /// <param name="sourcePath">Relative or absolute path to the <c>.vcdbs</c> fixture; relative
    /// paths resolve against <paramref name="baseDir"/>.</param>
    /// <param name="baseDir">Base directory for resolving a relative source path.</param>
    /// <param name="dataPath">The scratch data path to copy into.</param>
    /// <exception cref="AtlasSetupException">Thrown when the source file does not exist.</exception>
    public static void SeedWorldSave(string sourcePath, string baseDir, string dataPath)
    {
        string source = Path.GetFullPath(sourcePath, baseDir);
        if (!File.Exists(source))
        {
            throw new AtlasSetupException($"World save not found: {sourcePath}");
        }

        string savesDir = Path.Combine(dataPath, "Saves");
        Directory.CreateDirectory(savesDir);
        File.Copy(source, Path.Combine(savesDir, WorldSaveFileName), overwrite: true);
    }

    /// <summary>Resolves and copies data file seeds into the data path.</summary>
    /// <param name="seeds">The seeds to copy, in declaration order; on a name collision the
    /// later seed's file overwrites the earlier one's.</param>
    /// <param name="baseDir">Base directory for resolving relative source paths.</param>
    /// <param name="dataPath">The scratch data path to copy into.</param>
    /// <exception cref="AtlasSetupException">Thrown when one or more source paths do not exist,
    /// or when a target path escapes the data path.</exception>
    public static void Seed(IReadOnlyList<DataFileSeed> seeds, string baseDir, string dataPath)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        var missing = new List<string>();
        var resolved = new List<(string Source, string TargetDir)>();
        foreach (DataFileSeed seed in seeds)
        {
            string source = Path.GetFullPath(seed.SourcePath, baseDir);
            if (File.Exists(source) || Directory.Exists(source))
            {
                resolved.Add((source, ResolveTargetDir(seed, dataPath)));
            }
            else
            {
                missing.Add(seed.SourcePath);
            }
        }

        if (missing.Count > 0)
        {
            throw new AtlasSetupException("Data file path(s) not found: " + string.Join(", ", missing));
        }

        foreach ((string source, string targetDir) in resolved)
        {
            if (File.Exists(source))
            {
                Directory.CreateDirectory(targetDir);
                File.Copy(source, Path.Combine(targetDir, Path.GetFileName(source)), overwrite: true);
            }
            else
            {
                ModStager.CopyTree(new DirectoryInfo(source), targetDir);
            }
        }
    }

    /// <summary>Resolves a seed's target directory under the data path, rejecting escapes: a
    /// rooted <see cref="DataFileSeed.TargetPath"/> or one whose <c>..</c> segments climb out of
    /// the data path would silently write outside the scratch sandbox.</summary>
    private static string ResolveTargetDir(DataFileSeed seed, string dataPath)
    {
        string dataRoot = Path.GetFullPath(dataPath);
        string targetDir = Path.GetFullPath(Path.Combine(dataRoot, seed.TargetPath));
        if (targetDir != dataRoot &&
            !targetDir.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new AtlasSetupException(
                $"Data file target path '{seed.TargetPath}' escapes the server data path: it must " +
                "be a relative path staying inside it, e.g. \"ModConfig\".");
        }

        return targetDir;
    }
}
