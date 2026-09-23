using Vintagestory.API.Common;

namespace Atlas.Bridge;

/// <summary>Harness-side mod: publishes the loaded mod list to Atlas as early as the engine
/// allows, so <c>ServerHost.SubscribeModLoggers</c> can subscribe to every mod's own
/// <c>Mod.Logger</c> before that mod's own startup code runs.</summary>
/// <remarks>Split out from <see cref="BridgeModSystem"/> (which stays at the default
/// <c>ExecuteOrder</c>, 0.1) so that only this one, narrow responsibility - publishing the mod
/// list - runs ahead of every other mod's <c>StartPre</c>/<c>StartServerSide</c>. Moving the
/// whole bridge that early would also move <see cref="BridgeModSystem.StartServerSide"/>'s tick
/// listener ahead of engine systems below 0.1 (EntityPartitioning, Core, ErrorReporter,
/// SurvivalCoreSystem, ModJsonPatchLoader) and every consumer mod, changing which tick a
/// scenario's <c>Until</c> predicate first sees a given state in - a behavior change this
/// feature does not need. See the same assembly-instance note on <see cref="BridgeModSystem"/>
/// for why this reaches the host through <see cref="BridgeRendezvous"/>'s AppDomain data slots
/// rather than calling into it directly.</remarks>
public sealed class BridgeModsPreSystem : ModSystem
{
    /// <inheritdoc/>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc/>
    /// <remarks>Far below any mod's default (0.1) or any worldgen part's own value, so this
    /// always runs first, among all mods: every mod object (and so its own <c>Mod.Logger</c>)
    /// already exists by <c>LoadMods</c> time, so publishing that list this early (see
    /// <see cref="StartPre"/>) lets the host subscribe to a mod's own logger before that mod's
    /// <c>StartPre</c> or <c>StartServerSide</c> ever runs, not just before its later asset
    /// loading.</remarks>
    public override double ExecuteOrder() => -1_000_000d;

    /// <inheritdoc/>
    /// <remarks>Runs on the game thread, before any mod's own <c>StartPre</c>/<c>StartServerSide</c>
    /// (see <see cref="ExecuteOrder"/>).</remarks>
    public override void StartPre(ICoreAPI api)
    {
        if (AppDomain.CurrentDomain.GetData(BridgeRendezvous.ModsPreSlot) is Action<object> publishModsPre)
        {
            publishModsPre(api.ModLoader.Mods);
        }
    }
}
