using System.Text;
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

    /// <summary>Name of the folder the engine keeps world saves in, under the server data path.
    /// The engine's own convention, so every Atlas path that points at a save agrees on it.</summary>
    internal const string SavesFolderName = "Saves";

    // Strict, so a file that holds a token but is not UTF-8 fails by name instead of being
    // re-encoded with replacement characters; no BOM of its own, so one the file has (it decodes
    // to U+FEFF and is written back) is the only one in the result.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

        string savesDir = Path.Combine(dataPath, SavesFolderName);
        Directory.CreateDirectory(savesDir);
        File.Copy(source, Path.Combine(savesDir, WorldSaveFileName), overwrite: true);
    }

    /// <summary>Resolves and copies data file seeds into the data path, replacing each
    /// <c>{{atlas:port:NAME}}</c> token a file holds with a free port.</summary>
    /// <param name="seeds">The seeds to copy, in declaration order; on a name collision the
    /// later seed's file overwrites the earlier one's.</param>
    /// <param name="baseDir">Base directory for resolving relative source paths.</param>
    /// <param name="dataPath">The scratch data path to copy into.</param>
    /// <param name="ports">Where the tokens' ports are kept, so the host can answer the
    /// scenario's lookup.</param>
    /// <exception cref="AtlasSetupException">Thrown when one or more source paths do not exist,
    /// when a target path escapes the data path, or when a file holds a malformed token or a
    /// token and is not UTF-8 text.</exception>
    public static void Seed(
        IReadOnlyList<DataFileSeed> seeds, string baseDir, string dataPath, DataFilePorts ports)
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
                CopyFile(source, Path.Combine(targetDir, Path.GetFileName(source)), ports);
            }
            else
            {
                ModStager.CopyTree(new DirectoryInfo(source), targetDir, (file, target) => CopyFile(file.FullName, target, ports));
            }
        }
    }

    /// <summary>Copies one data file. A file with no token in it is copied as it is, after a
    /// buffered scan that never holds it whole; one with a token is read, decoded as UTF-8,
    /// resolved and written back, so everything around the token (the BOM, the line endings)
    /// stays as the fixture has it.</summary>
    private static void CopyFile(string source, string target, DataFilePorts ports)
    {
        bool mayHoldToken;
        using (FileStream stream = File.OpenRead(source))
        {
            mayHoldToken = DataFilePorts.MayHoldToken(stream);
        }

        if (!mayHoldToken)
        {
            File.Copy(source, target, overwrite: true);
            return;
        }

        byte[] bytes = File.ReadAllBytes(source);
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new AtlasSetupException(
                $"Data file '{source}' holds '{{{{atlas:' (in any case) but is not UTF-8 text: Atlas only resolves tokens in UTF-8 files.");
        }

        File.WriteAllText(target, ports.Resolve(text, source), StrictUtf8);
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
