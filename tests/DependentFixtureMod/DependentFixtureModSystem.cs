using DependencyLibraryFixture;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Dependent Fixture",
    "dependentfixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture for Atlas's staged-mod dependency check: a mod that uses a library staged next to it.")]

namespace DependentFixtureMod;

/// <summary>A mod whose only behavior is to use a type of its library at start-up, which makes the
/// runtime bind that library before the world is ready.</summary>
public sealed class DependentFixtureModSystem : ModSystem
{
    /// <summary>Gets what the library's type reported at start-up, or 0 before it ran.</summary>
    public int SharedValue { get; private set; }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api) => SharedValue = new SharedType().Value;
}
