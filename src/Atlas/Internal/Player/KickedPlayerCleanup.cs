using System.Net;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Internal.Player;

/// <summary>Completes the engine's disconnect teardown when the server drops a joined test
/// player (kick, ban), so the player cannot linger as a half-removed zombie.</summary>
/// <remarks>
/// <para>Why this exists: mods commonly kick from a thread-pool thread (e.g. after awaiting an
/// HTTP call inside a PlayerJoin handler), and <c>ServerMain.DisconnectPlayer</c> is not safe
/// off the game thread - <c>ServerMain.FrameProfiler</c> is <c>[ThreadStatic]</c>, so
/// <c>DespawnEntity</c> hits a <see cref="NullReferenceException"/> on
/// <c>FrameProfiler.Mark</c> right after <c>Entity.OnEntityDespawn</c> ran. The teardown then
/// aborts with the audit line already written and PlayerDisconnect already fired, but BEFORE
/// <c>LoadedEntities</c> removal, <c>Clients</c> removal and <c>player.client = null</c>. The
/// result (confirmed empirically against VS 1.22.3): the player stays in
/// <c>AllOnlinePlayers</c> with <c>ConnectionState == Admitted</c>, its half-despawned entity
/// keeps ticking (spamming e.g. "Exception thrown while calculating near heat source strength"
/// every tick in survival worlds), and the kicking mod usually swallows the exception in its
/// own catch.</para>
/// <para>A real TCP client self-heals from this exact abort: the kicked client receives the
/// disconnect packet, closes its socket, and the socket-close event re-runs
/// <c>DisconnectPlayer</c> on the game thread, which completes cleanly. Atlas's dummy socket
/// pair has no close semantics (<c>DummyNetConnection.Shutdown</c>/<c>Close</c> are no-ops and
/// nothing ever drains the client-side buffer), so that second, correcting run never happens.
/// This class supplies the missing half: it schedules a game-thread check that re-runs
/// <c>DisconnectPlayer</c> if a dropped client is still registered - the same "second run on
/// the game thread" a real client's socket close would have produced.</para>
/// <para>The check is scheduled from two triggers: every PlayerDisconnect for this player,
/// plus once unconditionally at arm time as a safety net. <c>WorldSession.JoinPlayer</c> arms
/// this class BEFORE sending the RequestJoin packet, i.e. before PlayerJoin (the first event
/// that hands the player to a mod) can fire, so the PlayerDisconnect subscription is
/// guaranteed to exist by the time any kick can happen: an event that fired before the
/// subscription would be unobservable. A check tells "dropped" apart from "healthy" by the
/// player's dummy UDP endpoint registration: the teardown's system pass always removes it
/// (systems run before the despawn crash point), and nothing else ever does while the player
/// is online.</para>
/// <para>Timing caveat the retry logic exists for: <c>DisconnectPlayer</c> fires
/// PlayerDisconnect BEFORE its system pass removes the UDP endpoint, so a check that runs
/// while the (possibly preempted) kicking thread sits between those two points sees a player
/// that is still registered with its endpoint present: indistinguishable from healthy by
/// state alone. When the check was triggered by an observed PlayerDisconnect that verdict is
/// known to be wrong (a teardown is in flight), so the check re-schedules itself until the
/// teardown settles: it either completes cleanly (client unregistered) or aborts at the
/// despawn crash point (endpoint removed, client still registered). On loaded CI runners the
/// stall between the event and the system pass routinely outlives a single re-check, which is
/// why one-shot checking flaked there.</para>
/// </remarks>
internal static class KickedPlayerCleanup
{
    /// <summary>Delay between re-checks while an observed disconnect's teardown is still in
    /// flight, in milliseconds. Spaced by real time via <c>RegisterCallback</c>: enqueue-hopping
    /// cannot space anything, because <c>ProcessMainThreadTasks</c> drains tasks enqueued during
    /// the drain within the same pass.</summary>
    private const int InFlightRecheckDelayMs = 50;

    /// <summary>Upper bound on re-checks while an observed disconnect's teardown is still in
    /// flight (100 x 50 ms = 5 s of kicking-thread stall tolerance). The bound only exists to
    /// keep a pathological never-settling teardown (a system throwing before the UDP pass, which
    /// has never been observed) from re-scheduling forever.</summary>
    private const int MaxInFlightRechecks = 100;

    /// <summary>What a game-thread check concluded about the player's disconnect state.</summary>
    private enum CheckOutcome
    {
        /// <summary>Still registered, endpoint present, no disconnect observed: nothing to do.</summary>
        Healthy,

        /// <summary>Still registered, endpoint present, but a PlayerDisconnect for this player
        /// was observed: the teardown is in flight on another thread and has not yet reached the
        /// system pass; check again later.</summary>
        TeardownInFlight,

        /// <summary>The player is verifiably gone and this player's Atlas-side claims were
        /// released.</summary>
        Removed,
    }

    /// <summary>Arms the cleanup for one joined test player.</summary>
    /// <param name="api">The live server API, used to observe PlayerDisconnect.</param>
    /// <param name="server">The live server to complete the teardown on.</param>
    /// <param name="client">The connected client backing the joined test player.</param>
    /// <param name="connection">The player's dummy network endpoints, for restoring the UDP
    /// registration the aborted run already removed and for releasing the TCP slot.</param>
    /// <param name="onRemoved">Callback run on the game thread once the player is verifiably
    /// gone from the server, so the caller can release its own bookkeeping (e.g. free the
    /// joined-name claim to allow a rejoin under the same name). Runs at most once.</param>
    /// <remarks>Runs on the game thread (join-time). The PlayerDisconnect handler itself runs on
    /// whatever thread the kicking mod called <c>Disconnect</c> from, so it only enqueues; all
    /// actual work happens in <see cref="Check"/> on the game thread.</remarks>
    public static void Arm(
        ICoreServerAPI api,
        ServerMain server,
        ConnectedClient client,
        DummyPlayerConnection connection,
        Action onRemoved)
    {
        string playerUid = client.Player.PlayerUID;
        bool finalized = false;

        void RunCheck(bool disconnectObserved, int rechecksLeft)
        {
            if (finalized)
            {
                return;
            }

            switch (Check(server, client, connection, disconnectObserved))
            {
                case CheckOutcome.Removed:
                    finalized = true;
                    onRemoved();
                    break;
                case CheckOutcome.TeardownInFlight when rechecksLeft > 0:
                    // The kicking thread is somewhere between the PlayerDisconnect event and
                    // the system pass; give it real time to progress. RegisterCallback fires on
                    // a later game tick - re-enqueueing instead would be useless, because
                    // ProcessMainThreadTasks drains tasks enqueued during the drain within the
                    // same pass, burning every re-check microseconds apart while the kicking
                    // thread has not moved.
                    api.Event.RegisterCallback(
                        _ => RunCheck(disconnectObserved: true, rechecksLeft - 1),
                        InFlightRecheckDelayMs);
                    break;
                case CheckOutcome.TeardownInFlight:
                    ServerMain.Logger.Error(
                        "[Atlas] Disconnect teardown for test player {0} (client {1}) never " +
                        "settled after {2} re-checks; giving up. The player may linger as a " +
                        "zombie.",
                        client.PlayerName,
                        client.Id,
                        MaxInFlightRechecks);
                    break;
            }
        }

        // The PlayerDisconnect handler fires mid-DisconnectPlayer on whatever thread the kicking
        // mod called Disconnect from, so it must not touch server state itself: it hops to the
        // game thread through the (lock-protected, thread-safe) main-thread task queue. The
        // first check often runs while the straight-line (no awaits) DisconnectPlayer is still
        // executing; Check() recognizes that as TeardownInFlight and RunCheck polls until the
        // teardown settles (finished or aborted).
        api.Event.PlayerDisconnect += disconnected =>
        {
            if (disconnected.PlayerUID == playerUid)
            {
                server.EnqueueMainThreadTask(
                    () => RunCheck(disconnectObserved: true, MaxInFlightRechecks));
            }
        };

        // Safety net for a drop that happened between the join being observed and Arm running.
        // Arm runs before the RequestJoin packet is even sent (so before PlayerJoin can hand the
        // player to a kicking mod), which makes this window mod-free in practice; and Check()
        // no-ops on a healthy player, so scheduling it once unconditionally is safe.
        server.EnqueueMainThreadTask(() => RunCheck(disconnectObserved: false, rechecksLeft: 0));
    }

    /// <summary>Finishes the removal of <paramref name="client"/> if the server has dropped it:
    /// re-runs the engine teardown when the first run aborted halfway, then releases the TCP
    /// slot.</summary>
    /// <param name="server">The live server.</param>
    /// <param name="client">The client to verify removal of.</param>
    /// <param name="connection">The player's dummy network endpoints.</param>
    /// <param name="disconnectObserved">Whether this check was triggered by an observed
    /// PlayerDisconnect for this player. Disambiguates the "still registered, endpoint present"
    /// state: without an observed disconnect it means a healthy player; with one it means the
    /// teardown is in flight on another thread and has not reached the system pass yet (the
    /// event fires before the endpoint removal), so the caller must check again.</param>
    /// <returns>The settled or in-flight outcome; see <see cref="CheckOutcome"/>.</returns>
    /// <remarks>Runs on the game thread. "Dropped but still registered" is recognized by the
    /// missing dummy UDP endpoint registration: <c>DisconnectPlayer</c>'s system pass removes
    /// it before the off-thread despawn crash point, and nothing else ever removes it while
    /// the player is online.</remarks>
    private static CheckOutcome Check(
        ServerMain server,
        ConnectedClient client,
        DummyPlayerConnection connection,
        bool disconnectObserved)
    {
        bool stillRegistered = server.Clients.TryGetValue(client.Id, out ConnectedClient? registered)
            && ReferenceEquals(registered, client);
        var endpoint = new IPEndPoint(IPAddress.Loopback, client.Id);
        bool teardownStarted = !connection.UdpServer.EndPoints.ContainsKey(endpoint);

        if (stillRegistered && !teardownStarted)
        {
            return disconnectObserved ? CheckOutcome.TeardownInFlight : CheckOutcome.Healthy;
        }

        if (stillRegistered)
        {
            // The aborted run already ran every ServerSystem's OnPlayerDisconnect, including the
            // UDP system's, which removed this player's endpoint registration. Re-running
            // DisconnectPlayer repeats that system pass, and DummyUdpNetServer.Remove indexes
            // its reverse dictionary unguarded - so restore the registration first or the
            // re-run dies on KeyNotFoundException before ever reaching the entity despawn. The
            // endpoint is reconstructible because RegisterUdpEndpoint derives it from the
            // client id (the port IS the id).
            connection.UdpServer.Add(endpoint, client.Id);

            ServerMain.Logger.Notification(
                "[Atlas] Completing aborted disconnect teardown for test player {0} (client {1}): " +
                "the kick ran off the game thread and died mid-teardown, which would leave a " +
                "zombie player; re-running DisconnectPlayer on the game thread.",
                client.PlayerName,
                client.Id);
            server.DisconnectPlayer(client);
        }

        DummyClientConnector.ReleaseSlot(server, connection);
        return CheckOutcome.Removed;
    }
}
