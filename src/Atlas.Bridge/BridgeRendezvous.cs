using Vintagestory.API.Server;

namespace Atlas.Bridge;

/// <summary>Engine-side rendezvous between the Atlas engine and the bridge mod instance the
/// game's ModLoader creates. The ModLoader loads a COPY of AtlasBridge.dll (staged into its own
/// folder so it does not scan the consumer's bin directory), which means the mod's assembly
/// instance is distinct from the engine's and does not share this class's statics. To bridge
/// that gap, <see cref="Reset"/> installs delegates into AppDomain data slots keyed by name;
/// the mod side (<see cref="BridgeModSystem"/>) reads those slots instead of referencing this
/// type directly. AppDomain data slots are identity-agnostic: they hold framework-typed
/// delegates (<see cref="Action"/>, <see cref="Action{T}"/>) regardless of which assembly
/// instance created them.</summary>
public static class BridgeRendezvous
{
    private static TaskCompletionSource<ICoreServerAPI> _api = NewTcs();

    /// <summary>Raised once per server tick.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public static event Action? TickFired;

    /// <summary>Completed by the mod when the server API is available.</summary>
    public static Task<ICoreServerAPI> ApiReady => _api.Task;

    /// <summary>Must be called by the engine before each server boot. Resets the rendezvous
    /// state and (re)installs the AppDomain data slots the mod-side copy of AtlasBridge.dll
    /// uses to reach this instance without sharing its assembly identity.</summary>
    public static void Reset()
    {
        _api = NewTcs();
        TickFired = null;

        AppDomain.CurrentDomain.SetData("atlas.bridge.publishApi", (Action<object>)(o => PublishApi((ICoreServerAPI)o)));
        AppDomain.CurrentDomain.SetData("atlas.bridge.onTick", (Action)NotifyTick);
    }

    /// <summary>Completes <see cref="ApiReady"/> with the live server API.</summary>
    /// <param name="api">The server API captured by the bridge mod.</param>
    public static void PublishApi(ICoreServerAPI api) => _api.TrySetResult(api);

    /// <summary>Raises <see cref="TickFired"/>; called by the bridge mod's tick listener.</summary>
    public static void NotifyTick() => TickFired?.Invoke();

    private static TaskCompletionSource<ICoreServerAPI> NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
