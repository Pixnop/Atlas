using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Bridge;

/// <summary>Host-side rendezvous between the Atlas host layer and the bridge mod instance the
/// game's ModLoader creates. The ModLoader loads a COPY of AtlasBridge.dll (staged into its own
/// folder so it does not scan the consumer's bin directory), which means the mod's assembly
/// instance is distinct from the host's and does not share this class's statics. To bridge
/// that gap, <see cref="Reset"/> installs delegates into AppDomain data slots keyed by name;
/// the mod side (<see cref="BridgeModSystem"/>) reads those slots instead of referencing this
/// type directly. AppDomain data slots are identity-agnostic: they hold framework-typed
/// delegates (<see cref="Action"/>, <see cref="Action{T}"/>) regardless of which assembly
/// instance created them.</summary>
internal static class BridgeRendezvous
{
    /// <summary>Name of the AppDomain data slot holding the delegate the mod calls once with the
    /// live server API.</summary>
    /// <remarks>A <c>const</c>, not a <c>static readonly</c>, on purpose: the compiler inlines
    /// its value into both assembly copies of this dll (the engine's and the one the game's
    /// ModLoader loads), which is what makes the two sides agree on the slot name without
    /// sharing any assembly identity. Renaming the value therefore breaks the rendezvous unless
    /// both copies are rebuilt together.</remarks>
    internal const string PublishApiSlot = "atlas.bridge.publishApi";

    /// <summary>Name of the AppDomain data slot holding the delegate the mod's tick listener
    /// calls each server tick. Const for the same reason as <see cref="PublishApiSlot"/>.</summary>
    internal const string TickSlot = "atlas.bridge.onTick";

    /// <summary>Name of the AppDomain data slot holding the delegate the mod's
    /// <c>StartPre</c> calls with every mod the engine has loaded so far. Const for the same
    /// reason as <see cref="PublishApiSlot"/>.</summary>
    internal const string ModsPreSlot = "atlas.bridge.modsPre";

    private static TaskCompletionSource<ICoreServerAPI> _api = NewTcs();

    /// <summary>Raised once per server tick.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public static event Action? TickFired;

    /// <summary>Raised once, from the bridge mod's own <c>StartPre</c> (lowest
    /// <c>ExecuteOrder</c>, so before any other mod's <c>StartPre</c> or <c>StartServerSide</c>),
    /// with every mod the engine has loaded so far: the earliest point at which every mod object
    /// (and so its own <c>Mod.Logger</c>) exists at all, since <c>ModLoader.LoadMods</c> already
    /// ran by then. See <c>ServerHost.SubscribeModLoggers</c>.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public static event Action<IEnumerable<Mod>>? ModsPre;

    /// <summary>Completed by the mod when the server API is available.</summary>
    public static Task<ICoreServerAPI> ApiReady => _api.Task;

    /// <summary>Must be called by the host before each server boot. Resets the rendezvous
    /// state and (re)installs the AppDomain data slots the mod-side copy of AtlasBridge.dll
    /// uses to reach this instance without sharing its assembly identity.</summary>
    public static void Reset()
    {
        _api = NewTcs();
        TickFired = null;
        ModsPre = null;

        AppDomain.CurrentDomain.SetData(PublishApiSlot, (Action<object>)(o => PublishApi((ICoreServerAPI)o)));
        AppDomain.CurrentDomain.SetData(TickSlot, (Action)NotifyTick);
        AppDomain.CurrentDomain.SetData(ModsPreSlot, (Action<object>)(o => NotifyModsPre((IEnumerable<Mod>)o)));
    }

    /// <summary>Completes <see cref="ApiReady"/> with the live server API.</summary>
    /// <param name="api">The server API captured by the bridge mod.</param>
    public static void PublishApi(ICoreServerAPI api) => _api.TrySetResult(api);

    /// <summary>Raises <see cref="TickFired"/>; called by the bridge mod's tick listener.</summary>
    public static void NotifyTick() => TickFired?.Invoke();

    /// <summary>Raises <see cref="ModsPre"/>; called by the bridge mod's own <c>StartPre</c>.</summary>
    /// <param name="mods">Every mod the engine has loaded so far.</param>
    public static void NotifyModsPre(IEnumerable<Mod> mods) => ModsPre?.Invoke(mods);

    private static TaskCompletionSource<ICoreServerAPI> NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
