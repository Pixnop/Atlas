namespace Atlas.Internal.Bootstrap;

/// <summary>What a scenario assembly says about the game it was compiled against, read by the
/// xUnit adapter and handed to the host.</summary>
/// <param name="Version">The game version stamped into the assembly at build
/// (<c>build/Atlas.E2E.targets</c>), or <see langword="null"/> when the assembly carries none.</param>
/// <param name="Required">Whether the assembly asked, with
/// <c>[assembly: AtlasRequireCompiledGameVersion]</c>, for a boot on that exact version.</param>
internal sealed record CompiledGameVersion(string? Version, bool Required);
