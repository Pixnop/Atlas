using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>The resolved world configuration and mod set for a scenario class, derived from
/// <see cref="AtlasWorldAttribute"/> and <see cref="AtlasModsAttribute"/>.</summary>
/// <param name="Options">World configuration to boot the embedded server with.</param>
/// <param name="ModPaths">Mod paths to stage, assembly-level mods first, then class-level mods.</param>
/// <param name="ModBaseDir">Base directory used to resolve relative mod paths.</param>
internal sealed record AtlasHostRecipe(WorldOptions Options, IReadOnlyList<string> ModPaths, string ModBaseDir);
