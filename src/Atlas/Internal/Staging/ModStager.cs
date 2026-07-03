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
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(stagingDir, Path.GetFileName(source)), overwrite: true);
            }
            else
            {
                CopyTree(new DirectoryInfo(source), Path.Combine(stagingDir, Path.GetFileName(source)));
            }
        }
    }

    private static void CopyTree(DirectoryInfo from, string to)
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
