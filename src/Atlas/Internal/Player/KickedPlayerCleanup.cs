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
/// <para>The check is scheduled from two triggers: every PlayerDisconnect for this player
/// (kicks after the join completed), plus once unconditionally at arm time - a kick can land in
/// the window between PlayerJoin firing inside the engine's request-join handler and
/// <see cref="Arm"/> subscribing (JoinPlayer is still awaiting inventory wiring then), and an
/// event that already fired is gone. The arm-time check tells "kicked before arming" apart
/// from "healthy" by the player's dummy UDP endpoint registration: the aborted teardown's
/// system pass always removes it (systems run before the despawn crash point), and nothing
/// else ever does.</para>
/// </remarks>
internal static class KickedPlayerCleanup
{
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
    /// actual work happens in <see cref="Complete"/> on the game thread.</remarks>
    public static void Arm(
        ICoreServerAPI api,
        ServerMain server,
        ConnectedClient client,
        DummyPlayerConnection connection,
        Action onRemoved)
    {
        string playerUid = client.Player.PlayerUID;
        bool finalized = false;

        void ScheduleCompletion()
        {
            // Double-hop on purpose: a PlayerDisconnect fires mid-DisconnectPlayer, possibly on
            // a thread-pool thread whose (doomed) teardown is still executing. One enqueue
            // could run the check within the same Process() pass, potentially concurrent with
            // that off-thread teardown's remaining statements; hopping through two main-thread
            // task batches guarantees at least one full pass in between, by which time the
            // straight-line (no awaits) DisconnectPlayer has either finished or aborted.
            // Complete() then observes the settled outcome.
            server.EnqueueMainThreadTask(
                () => server.EnqueueMainThreadTask(
                    () =>
                    {
                        if (!finalized && Complete(server, client, connection))
                        {
                            finalized = true;
                            onRemoved();
                        }
                    }));
        }

        api.Event.PlayerDisconnect += disconnected =>
        {
            if (disconnected.PlayerUID == playerUid)
            {
                ScheduleCompletion();
            }
        };

        // Cover a kick that already happened: PlayerJoin fires inside the engine's request-join
        // handling, while JoinPlayer is still awaiting inventory wiring - a mod kicking from its
        // PlayerJoin handler (or a fast background continuation of it) can therefore fire
        // PlayerDisconnect before the subscription above exists. Complete() no-ops on a healthy
        // player, so scheduling it once unconditionally is safe.
        ScheduleCompletion();
    }

    /// <summary>Finishes the removal of <paramref name="client"/> if the server has dropped it:
    /// re-runs the engine teardown when the first run aborted halfway, then releases the TCP
    /// slot.</summary>
    /// <param name="server">The live server.</param>
    /// <param name="client">The client to verify removal of.</param>
    /// <param name="connection">The player's dummy network endpoints.</param>
    /// <returns><see langword="true"/> when the player is gone and this player's Atlas-side
    /// claims were released; <see langword="false"/> when the player is healthy and untouched
    /// (the arm-time check for a not-kicked player).</returns>
    /// <remarks>Runs on the game thread. "Kicked but still registered" is recognized by the
    /// missing dummy UDP endpoint registration: <c>DisconnectPlayer</c>'s system pass removes
    /// it before the off-thread despawn crash point, and nothing else ever removes it while
    /// the player is online.</remarks>
    private static bool Complete(ServerMain server, ConnectedClient client, DummyPlayerConnection connection)
    {
        bool stillRegistered = server.Clients.TryGetValue(client.Id, out ConnectedClient? registered)
            && ReferenceEquals(registered, client);
        var endpoint = new IPEndPoint(IPAddress.Loopback, client.Id);
        bool teardownStarted = !connection.UdpServer.EndPoints.ContainsKey(endpoint);

        if (stillRegistered && !teardownStarted)
        {
            return false; // Healthy, still-connected player: nothing to complete.
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
        return true;
    }
}
