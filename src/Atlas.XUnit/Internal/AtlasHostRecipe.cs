using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>The resolved world configuration, mod set and data file seeds for a scenario class,
/// derived from <see cref="AtlasWorldAttribute"/>, <see cref="AtlasModsAttribute"/> and
/// <see cref="AtlasDataFilesAttribute"/>.</summary>
/// <param name="Options">World configuration to boot the embedded server with.</param>
/// <param name="ModPaths">Mod paths to stage, assembly-level mods first, then class-level mods.</param>
/// <param name="ModBaseDir">Base directory used to resolve relative mod and data file paths.</param>
/// <param name="DataFiles">Data files to seed into the scratch data path before boot,
/// assembly-level seeds first, then class-level seeds.</param>
internal sealed record AtlasHostRecipe(
    WorldOptions Options,
    IReadOnlyList<string> ModPaths,
    string ModBaseDir,
    IReadOnlyList<DataFileSeed> DataFiles);
