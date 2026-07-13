namespace Atlas.Internal.Hosting;

/// <summary>The pure decision core of the pump's engine-stop watch (see
/// <c>ServerHost.GameThreadMain</c>): classifies an observed engine stop against Atlas's own
/// stop request, and words the crash surfaced when the engine stopped itself. Kept free of
/// engine types so the classification and the message are testable without booting a server
/// (the <see cref="AssetsBuildSettle"/> pattern).</summary>
/// <remarks>Background: the engine's <c>ServerThread.Process</c> reacts to an unhandled
/// exception in any of its server threads (chunkdbthread, compresschunks, ...) by logging
/// "Caught unhandled exception in thread '...'" and enqueuing
/// <c>ServerMain.Stop("Exception during Process", ...)</c> onto the main thread. <c>Stop</c>
/// sets the public <c>ServerMain.stopped</c> flag first thing (identical on 1.20.12, 1.21.7 and
/// 1.22.3), runs the shutdown sequence and leaves the server suspended, so every further
/// <c>ServerMain.Process()</c> call just sleeps. A pump that keeps calling <c>Process()</c> on
/// such a server spins silently forever; the watch turns that into a prompt crash instead.</remarks>
internal static class EngineStopDetection
{
    /// <summary>Classifies an observed engine stop: only a stop Atlas did not request is a
    /// crash. Atlas's own stop paths cancel the pump's stop token first and call the engine's
    /// <c>Stop</c> only after the pump loop has exited, so inside the loop an engine-side stop
    /// with no pending Atlas request can only be engine-initiated.</summary>
    /// <param name="engineStopped">The engine's stop flag (<c>ServerMain.stopped</c>).</param>
    /// <param name="atlasStopRequested">Whether Atlas's own stop token is already canceled.</param>
    /// <returns>Whether the pump must treat the state as a host crash.</returns>
    public static bool IsEngineInitiatedStop(bool engineStopped, bool atlasStopRequested)
        => engineStopped && !atlasStopRequested;

    /// <summary>Words the crash for an engine-initiated stop. The engine does not retain the
    /// stop reason string anywhere readable (it only logs it), so the message points at the
    /// server logs, where the engine wrote both the reason and, for the unhandled-exception
    /// case, the failing thread's stack.</summary>
    /// <param name="dataPath">The host's scratch data path, containing the server logs.</param>
    /// <returns>The crash message.</returns>
    public static string Describe(string dataPath)
        => "The embedded server stopped itself without Atlas requesting it. This is how the " +
           "engine reacts to a fatal server-side failure, most commonly an unhandled exception " +
           "in one of its server threads (stop reason 'Exception during Process'). The root " +
           "cause is in the engine's own log, not in this stack: check server-main.log under " +
           $"'{dataPath}/Logs'.";
}
