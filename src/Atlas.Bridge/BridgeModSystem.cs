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
/// <remarks>The game's ModLoader loads this class from a copy of AtlasBridge.dll staged into
/// its own folder, which is a distinct assembly instance from the one the engine references
/// via ProjectReference. That means this class must NOT call into <see cref="BridgeRendezvous"/>
/// directly: its statics live in a different assembly instance and would never be observed by
/// the engine. Instead, this class reaches the engine through AppDomain data slots that
/// <see cref="BridgeRendezvous.Reset"/> installs before boot. Those slots hold only
/// framework-typed delegates (<see cref="Action"/>, <see cref="Action{T}"/>), so no assembly
/// identity is involved in the handoff. Passing <see cref="ICoreServerAPI"/> itself as
/// <see cref="object"/> is safe because VintagestoryAPI.dll is loaded once, from the game
/// install, and shared by both sides.</remarks>
public sealed class BridgeModSystem : ModSystem
{
    /// <inheritdoc/>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc/>
    /// <remarks>Runs on the game thread.</remarks>
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (AppDomain.CurrentDomain.GetData("atlas.bridge.onTick") is Action onTick)
        {
            api.Event.RegisterGameTickListener(_ => onTick(), 1);
        }

        if (AppDomain.CurrentDomain.GetData("atlas.bridge.publishApi") is Action<object> publish)
        {
            publish(api);
        }
    }
}
