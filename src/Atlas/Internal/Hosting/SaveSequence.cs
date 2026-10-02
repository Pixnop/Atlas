using Atlas.Api;
using Atlas.Internal.Scheduling;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>The engine's forced save as a scenario can wait on it: wait until the save machinery
/// is idle, run <c>/autosavenow</c> and require its completion message, wait until the machinery
/// is idle again. Single owner of that sequence for the two callers that need it: the rollback
/// capture (<c>WorldSnapshot</c>) and <see cref="IWorldSession.SaveNow"/>.</summary>
/// <remarks>Only the three steps above live here. What else the rollback capture does around its
/// save (turning the timed autosave off, pausing the background chunk unloader, marking the
/// mini-dimension chunks dirty) stays with the capture: a forced save a scenario asks for has to
/// leave both background writers as they were and has to save exactly what a real autosave
/// would, or it would hide the persistence bugs it exists to catch.</remarks>
internal static class SaveSequence
{
    /// <summary>Bound on each wait for the engine's save machinery to go idle. Generous: an
    /// off-thread save of a large world is slow, and a timeout here fails the call rather than
    /// touching the database under a live writer.</summary>
    private const int IdleTimeoutTicks = 5000;

    /// <summary>Runs the whole sequence.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="server">The live embedded server.</param>
    /// <param name="chunkThread">The engine's chunk thread, whose off-thread save flag the waits read.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <param name="caller">Names the caller in the failure messages, for example
    /// <c>"World rollback"</c> or <c>"SaveNow"</c>.</param>
    /// <param name="beforeSave">Runs on the game thread between the first wait and the save
    /// command, or <see langword="null"/> for nothing.</param>
    /// <returns>A task that completes once the save has been written out.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the machinery is still busy at the end
    /// of either wait, or when the engine answers the command with anything but its completion
    /// message (it reports "not ready" and "backup in progress" as successes with other
    /// texts).</exception>
    public static async Task RunAsync(
        ICoreServerAPI api,
        ServerMain server,
        ChunkServerThread chunkThread,
        TickSource ticks,
        string caller,
        Action? beforeSave = null)
    {
        await WaitIdleAsync(server, chunkThread, ticks, caller, "before the forced save").ConfigureAwait(true);
        beforeSave?.Invoke();

        TextCommandResult result = await ConsoleCommands.ExecuteAsync(api, "/autosavenow", ConsoleCommands.Console()).ConfigureAwait(true);
        string message = result.StatusMessage ?? string.Empty;
        if (!message.Contains("Autosave completed", StringComparison.Ordinal))
        {
            throw new AtlasSetupException($"{caller}: the engine skipped the forced save: '{message}'.");
        }

        await WaitIdleAsync(server, chunkThread, ticks, caller, "after the forced save").ConfigureAwait(true);
    }

    /// <summary>Waits until no off-thread save is in flight and the engine reports itself ready
    /// to save, pumping the game thread meanwhile.</summary>
    /// <param name="server">The live embedded server.</param>
    /// <param name="chunkThread">The engine's chunk thread.</param>
    /// <param name="ticks">The tick source used to pump the game thread while waiting.</param>
    /// <param name="caller">Names the caller in the failure message.</param>
    /// <param name="stage">Human-readable stage name for the failure message.</param>
    /// <returns>A task that completes when the save machinery is idle.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the machinery is still busy after the
    /// bound.</exception>
    public static async Task WaitIdleAsync(
        ServerMain server,
        ChunkServerThread chunkThread,
        TickSource ticks,
        string caller,
        string stage)
    {
        try
        {
            await ticks.WaitUntilAsync(
                () => server.readyToAutoSave && !chunkThread.runOffThreadSaveNow,
                timeoutTicks: IdleTimeoutTicks).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException ex)
        {
            throw new AtlasSetupException(
                $"{caller}: save machinery still busy {stage} " +
                $"(readyToAutoSave={server.readyToAutoSave}, runOffThreadSaveNow={chunkThread.runOffThreadSaveNow}).",
                ex);
        }
    }
}
