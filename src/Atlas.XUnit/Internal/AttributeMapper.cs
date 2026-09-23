using System.Globalization;
using System.Reflection;
using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Maps <see cref="AtlasWorldAttribute"/>, <see cref="AtlasModsAttribute"/>,
/// <see cref="AtlasDataFilesAttribute"/> and <see cref="AtlasAllowBootDiagnosticAttribute"/>
/// metadata on a scenario class into an <see cref="AtlasHostRecipe"/>. Pure aside from one file
/// read: the MSBuild-generated mod manifest (see <see cref="ManifestFileName"/>), which is
/// either absent (no I/O effect beyond an existence check) or already written by the time any
/// test runs.</summary>
internal static class AttributeMapper
{
    /// <summary>Name of the file MSBuild's <c>WriteAtlasModManifest</c> target (in
    /// <c>build/Atlas.E2E.targets</c>) writes next to the test assembly, one absolute mod path per
    /// line, for every <c>ProjectReference</c> tagged <c>&lt;AtlasMod&gt;true&lt;/AtlasMod&gt;</c>.</summary>
    internal const string ManifestFileName = "atlas-mods.generated.txt";

    /// <summary>Builds the host recipe for the given scenario class.</summary>
    /// <param name="testClass">The scenario class, decorated with an optional <see cref="AtlasWorldAttribute"/>.</param>
    /// <returns>The resolved world options, mod paths (assembly mods, then class mods, then the
    /// MSBuild-generated manifest's paths, if present (all three skipped except class mods when
    /// <see cref="AtlasWorldAttribute.ExcludeAssemblyMods"/> is set)), mod base directory, and
    /// data file seeds (assembly-level, then class-level).</returns>
    public static AtlasHostRecipe Map(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        // An undecorated class runs against a default-constructed attribute rather than a second
        // copy of its defaults: the seed, world type and play style live on AtlasWorldAttribute
        // alone, so there is nowhere for the two to drift apart.
        AtlasWorldAttribute worldAttribute =
            testClass.GetCustomAttribute<AtlasWorldAttribute>() ?? new AtlasWorldAttribute();
        AtlasModsAttribute? modsAttribute = testClass.Assembly.GetCustomAttribute<AtlasModsAttribute>();

        var options = new WorldOptions
        {
            Seed = worldAttribute.Seed.ToString(CultureInfo.InvariantCulture),
            WorldType = worldAttribute.WorldType,
            PlayStyle = worldAttribute.PlayStyle,
            SaveFile = worldAttribute.SaveFile,
            StrictBootDiagnostics = worldAttribute.StrictBootDiagnostics,
            AllowedBootDiagnostics = MapAllowedBootDiagnostics(testClass),
        };

        string modBaseDir = Path.GetDirectoryName(testClass.Assembly.Location)!;
        var modPaths = new List<string>();
        if (!worldAttribute.ExcludeAssemblyMods && modsAttribute != null)
        {
            modPaths.AddRange(modsAttribute.Paths);
        }

        modPaths.AddRange(worldAttribute.Mods);

        if (!worldAttribute.ExcludeAssemblyMods)
        {
            modPaths.AddRange(ReadGeneratedManifest(modBaseDir));
        }

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

    /// <summary>Collects <see cref="AtlasAllowBootDiagnosticAttribute"/> rules, assembly-level
    /// first, then class-level; both apply (a class never loses an assembly-wide allowance).
    /// <see cref="AtlasAllowBootDiagnosticAttribute.Level"/> is carried through as-is (a plain
    /// string): resolving it against the engine's <c>EnumLogType</c> happens where that type is
    /// actually reachable, in <c>Atlas.Internal.Diagnostics.BootDiagnosticsAllowlist</c>, which is
    /// also why each rule carries <see cref="AllowedBootDiagnostic.DeclaredOn"/>: so a bad
    /// <c>Level</c> caught there can still name where it was declared.</summary>
    private static List<AllowedBootDiagnostic> MapAllowedBootDiagnostics(Type testClass)
    {
        var allowed = new List<AllowedBootDiagnostic>();
        string assemblyName = testClass.Assembly.GetName().Name ?? testClass.Assembly.FullName ?? "?";
        AppendAllowed(
            allowed, testClass.Assembly.GetCustomAttributes<AtlasAllowBootDiagnosticAttribute>(), $"assembly '{assemblyName}'");
        AppendAllowed(
            allowed, testClass.GetCustomAttributes<AtlasAllowBootDiagnosticAttribute>(), $"class '{testClass.FullName ?? testClass.Name}'");
        return allowed;
    }

    private static void AppendAllowed(
        List<AllowedBootDiagnostic> allowed, IEnumerable<AtlasAllowBootDiagnosticAttribute> attributes, string declaredOn)
    {
        foreach (AtlasAllowBootDiagnosticAttribute attribute in attributes)
        {
            allowed.Add(new AllowedBootDiagnostic(attribute.MessagePattern, attribute.Level, attribute.Source, declaredOn));
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
