using System.Globalization;
using System.Reflection;
using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Maps <see cref="AtlasWorldAttribute"/> and <see cref="AtlasModsAttribute"/> metadata on a
/// scenario class into an <see cref="AtlasHostRecipe"/>. Pure: no I/O, no server state.</summary>
internal static class AttributeMapper
{
    /// <summary>Builds the host recipe for the given scenario class.</summary>
    /// <param name="testClass">The scenario class, decorated with an optional <see cref="AtlasWorldAttribute"/>.</param>
    /// <returns>The resolved world options, mod paths (assembly mods first, then class mods), and mod base directory.</returns>
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

        return new AtlasHostRecipe(options, modPaths, modBaseDir);
    }
}
