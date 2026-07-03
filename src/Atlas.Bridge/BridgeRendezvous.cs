using Vintagestory.API.Server;

namespace Atlas.Bridge;

/// <summary>Static rendezvous between the Atlas engine and the bridge mod instance the
/// game's ModLoader creates. Works because both sides load AtlasBridge.dll from the same
/// path into the default AssemblyLoadContext.</summary>
public static class BridgeRendezvous
{
    private static TaskCompletionSource<ICoreServerAPI> _api = NewTcs();

    /// <summary>Raised once per server tick.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public static event Action? TickFired;

    /// <summary>Completed by the mod when the server API is available.</summary>
    public static Task<ICoreServerAPI> ApiReady => _api.Task;

    /// <summary>Must be called by the engine before each server boot.</summary>
    public static void Reset()
    {
        _api = NewTcs();
        TickFired = null;
    }

    /// <summary>Completes <see cref="ApiReady"/> with the live server API.</summary>
    /// <param name="api">The server API captured by the bridge mod.</param>
    public static void PublishApi(ICoreServerAPI api) => _api.TrySetResult(api);

    /// <summary>Raises <see cref="TickFired"/>; called by the bridge mod's tick listener.</summary>
    public static void NotifyTick() => TickFired?.Invoke();

    private static TaskCompletionSource<ICoreServerAPI> NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
