using Vintagestory.API.Common;

namespace StagingFixtureMod.Beta;

/// <summary>Does nothing but exist as a code mod, see the Alpha fixture.</summary>
public sealed class StagingFixtureBetaSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
}
