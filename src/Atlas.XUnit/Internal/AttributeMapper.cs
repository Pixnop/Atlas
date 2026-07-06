using System.Globalization;
using System.Reflection;
using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Maps <see cref="AtlasWorldAttribute"/>, <see cref="AtlasModsAttribute"/> and
/// <see cref="AtlasDataFilesAttribute"/> metadata on a
/// scenario class into an <see cref="AtlasHostRecipe"/>. Pure aside from one file read: the
/// MSBuild-generated mod manifest (see <see cref="ManifestFileName"/>), which is either absent
/// (no I/O effect beyond an existence check) or already written by the time any test runs.</summary>
internal static class AttributeMapper
{
    /// <summary>Name of the file MSBuild's <c>WriteAtlasModManifest</c> target (in
    /// <c>build/Atlas.E2E.targets</c>) writes next to the test assembly, one absolute mod path per
    /// line, for every <c>ProjectReference</c> tagged <c>&lt;AtlasMod&gt;true&lt;/AtlasMod&gt;</c>.</summary>
    internal const string ManifestFileName = "atlas-mods.generated.txt";

    /// <summary>Builds the host recipe for the given scenario class.</summary>
    /// <param name="testClass">The scenario class, decorated with an optional <see cref="AtlasWorldAttribute"/>.</param>
    /// <returns>The resolved world options, mod paths (assembly mods, then class mods, then the
    /// MSBuild-generated manifest's paths, if present), mod base directory, and data file seeds
    /// (assembly-level, then class-level).</returns>
    public static AtlasHostRecipe Map(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        var worldAttribute = testClass.GetCustomAttribute<AtlasWorldAttribute>();
        var modsAttribute = testClass.Assembly.GetCustomAttribute<AtlasModsAttribute>();

        var options = new WorldOptions
        {
            Seed = (worldAttribute?.Seed ?? 424242).ToString(CultureInfo.InvariantCulture),
            WorldType = worldAttribute?.WorldType ?? "superflat",
            PlayStyle = worldAttribute?.PlayStyle ?? "creativebuilding",
            SaveFile = worldAttribute?.SaveFile,
        };

        var modPaths = new List<string>();
        if (modsAttribute != null)
        {
            modPaths.AddRange(modsAttribute.Paths);
        }

        if (worldAttribute != null)
        {
            modPaths.AddRange(worldAttribute.Mods);
        }

        string modBaseDir = Path.GetDirectoryName(testClass.Assembly.Location)!;
        modPaths.AddRange(ReadGeneratedManifest(modBaseDir));

        return new AtlasHostRecipe(options, modPaths, modBaseDir, MapDataFiles(testClass));
    }

    /// <summary>Collects <see cref="AtlasDataFilesAttribute"/> seeds, assembly-level first, then
    /// class-level, so class-level files win when both seed the same target file name.</summary>
    private static List<DataFileSeed> MapDataFiles(Type testClass)
    {
        var dataFiles = new List<DataFileSeed>();
        AppendSeeds(dataFiles, testClass.Assembly.GetCustomAttributes<AtlasDataFilesAttribute>());
        AppendSeeds(dataFiles, testClass.GetCustomAttributes<AtlasDataFilesAttribute>());
        return dataFiles;
    }

    private static void AppendSeeds(List<DataFileSeed> dataFiles, IEnumerable<AtlasDataFilesAttribute> attributes)
    {
        foreach (AtlasDataFilesAttribute attribute in attributes)
        {
            dataFiles.AddRange(attribute.SourcePaths.Select(path => new DataFileSeed(path, attribute.TargetPath)));
        }
    }

    private static string[] ReadGeneratedManifest(string modBaseDir)
    {
        string manifestPath = Path.Combine(modBaseDir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        return File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
