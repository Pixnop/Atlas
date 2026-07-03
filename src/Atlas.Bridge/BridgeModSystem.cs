using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Bridge",
    "atlasbridge",
    Version = "0.1.0",
    Side = "Server",
    Description = "Atlas test-harness bridge; captures the server API and tick events.")]

namespace Atlas.Bridge;

/// <summary>Harness-side mod: hands the live <see cref="ICoreServerAPI"/> and tick events to Atlas.</summary>
public sealed class BridgeModSystem : ModSystem
{
    /// <inheritdoc/>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc/>
    /// <remarks>Runs on the game thread.</remarks>
    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.RegisterGameTickListener(_ => BridgeRendezvous.NotifyTick(), 1);
        BridgeRendezvous.PublishApi(api);
    }
}
