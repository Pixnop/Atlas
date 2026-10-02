using Vintagestory.API.Common;

namespace StagingFixtureMod.Alpha;

/// <summary>Does nothing but exist as a code mod: the check is that the folder it ships in is
/// staged whole (dll, modinfo.json, assets/), not what the system does.</summary>
public sealed class StagingFixtureAlphaSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
}
