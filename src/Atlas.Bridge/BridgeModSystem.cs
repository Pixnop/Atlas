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
/// its own folder, which is a distinct assembly instance from the one the Atlas host references
/// via ProjectReference. That means this class must NOT call into <see cref="BridgeRendezvous"/>
/// directly: its statics live in a different assembly instance and would never be observed by
/// the host. Instead, this class reaches the host through AppDomain data slots that
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
    /// <remarks>Far below any mod's default (0.1) or any worldgen part's own value, so this mod's
    /// <c>StartPre</c> always runs first, among all mods: every mod object (and so its own
    /// <c>Mod.Logger</c>) already exists by <c>LoadMods</c> time, so publishing that list this
    /// early (see <see cref="StartPre"/>) lets the host subscribe to a mod's own logger before
    /// that mod's <c>StartPre</c> or <c>StartServerSide</c> ever runs, not just before its later
    /// asset loading.</remarks>
    public override double ExecuteOrder() => -1_000_000d;

    /// <inheritdoc/>
    /// <remarks>Runs on the game thread, before any mod's own <c>Start()</c>/<c>StartServerSide()</c>
    /// (see <see cref="ExecuteOrder"/>).</remarks>
    public override void StartPre(ICoreAPI api)
    {
        if (AppDomain.CurrentDomain.GetData(BridgeRendezvous.ModsPreSlot) is Action<object> publishModsPre)
        {
            publishModsPre(api.ModLoader.Mods);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Runs on the game thread.</remarks>
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (AppDomain.CurrentDomain.GetData(BridgeRendezvous.TickSlot) is Action onTick)
        {
            api.Event.RegisterGameTickListener(_ => onTick(), 1);
        }

        if (AppDomain.CurrentDomain.GetData(BridgeRendezvous.PublishApiSlot) is Action<object> publish)
        {
            publish(api);
        }
    }
}
