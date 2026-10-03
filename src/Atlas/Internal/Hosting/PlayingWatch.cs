using Atlas.Api;
using Atlas.Internal.Bootstrap;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Detects that a connection reached <c>Playing</c> by polling its connection state
/// each tick, never by waiting for <c>api.Event.PlayerReady</c>.</summary>
/// <remarks><para>Why polling. The engine raises <c>PlayerReady</c> when it handles packet 29,
/// but only on its own core event manager. <c>CoreServerEventManager</c> overrides
/// <c>TriggerPlayerJoin</c>, <c>TriggerPlayerNowPlaying</c> and <c>TriggerPlayerLeave</c> to
/// forward to the mod-level manager and does not override <c>TriggerPlayerReady</c>, while the
/// mod-facing <c>api.Event.PlayerReady</c> subscribes to the mod-level one. The event therefore
/// never reaches a mod, and a wait built on it never ends. Measured on 1.22.3 with a real client:
/// the player was in state <c>Playing</c> and the subscribed handler had not run. The same code
/// is in 1.22.7, and <c>PlayingDetectionTests</c> pins the silence on dummy players. Before 1.22
/// the event does not exist at all. <c>PlayerNowPlaying</c> is raised
/// by packet 26, one packet earlier, and is no proxy for <c>Playing</c> either: a client that
/// withholds packet 29 (the character dialog does) is Connected, not Playing.</para>
/// <para>The state is the engine's own client state (<c>IServerPlayer.ConnectionState</c> reads
/// <c>ConnectedClient.State</c>), written by every join path, dummy or real, so reading it from
/// the game thread once per tick is cheap and exact. The
/// <c>Playing</c> value goes through <see cref="EngineCompat.ClientStatePlaying"/> and never the
/// enum literal, which 1.22 shifted.</para></remarks>
internal static class PlayingWatch
{
    /// <summary>Tells whether an engine client state is <c>Playing</c>.</summary>
    /// <param name="state">A client's state, from <c>ConnectedClient.State</c> or
    /// <c>IServerPlayer.ConnectionState</c>.</param>
    /// <returns><see langword="true"/> for <c>Playing</c>.</returns>
    internal static bool IsPlaying(EnumClientState state) => state == EngineCompat.ClientStatePlaying;

    /// <summary>Waits until <paramref name="find"/> names a player that is <c>Playing</c>, polling
    /// once per tick on the game thread.</summary>
    /// <param name="ticks">The host's tick source.</param>
    /// <param name="find">Picks the player to watch, or <see langword="null"/> while it is not
    /// there yet (a real client appears some seconds after it was started, under a name Atlas does
    /// not know). Called once per tick.</param>
    /// <param name="timeoutTicks">The bound, in ticks.</param>
    /// <returns>The player, once <c>Playing</c>.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when no such player reached
    /// <c>Playing</c> within <paramref name="timeoutTicks"/>; the caller says what it was waiting
    /// for.</exception>
    /// <remarks>Runs on the game thread, like every <see cref="TickSource"/> wait.</remarks>
    internal static async Task<IServerPlayer> WaitForPlayingAsync(
        TickSource ticks, Func<IServerPlayer?> find, int timeoutTicks = TickBounds.DefaultWait)
    {
        IServerPlayer? playing = null;
        await ticks.WaitUntilAsync(
            () =>
            {
                IServerPlayer? candidate = find();
                if (candidate == null || !IsPlaying(candidate.ConnectionState))
                {
                    return false;
                }

                playing = candidate;
                return true;
            },
            timeoutTicks).ConfigureAwait(true);
        return playing!;
    }
}
