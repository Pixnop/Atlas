using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Races a scenario task against a wall-clock timeout, off the game thread.</summary>
/// <remarks>Callers must await this from a continuation that does not run on the game thread's
/// <c>SynchronizationContext</c> (i.e. reached via <c>ConfigureAwait(false)</c> all the way down), so
/// that the <see cref="Task.WhenAny(Task,Task)"/> continuation still fires when the game thread itself
/// is the thing that is stuck.</remarks>
internal static class Watchdog
{
    /// <summary>Awaits <paramref name="scenario"/>, failing fast with
    /// <see cref="ScenarioTimeoutException"/> if it has not completed within
    /// <paramref name="timeoutMs"/>.</summary>
    /// <param name="scenario">The scenario task to await.</param>
    /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for
    /// <paramref name="scenario"/> to complete.</param>
    /// <param name="currentTick">Called to fetch the tick count to report on timeout; may return a
    /// stale value since it is typically read across threads for diagnostics only.</param>
    /// <returns>A task that completes when <paramref name="scenario"/> completes, or throws
    /// <see cref="ScenarioTimeoutException"/> if the timeout elapses first.</returns>
    /// <exception cref="ScenarioTimeoutException">Thrown when <paramref name="scenario"/> does not
    /// complete within <paramref name="timeoutMs"/>.</exception>
    public static async Task RunAsync(Task scenario, int timeoutMs, Func<int> currentTick)
    {
        Task winner = await Task.WhenAny(scenario, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (winner != scenario)
        {
            throw new ScenarioTimeoutException(
                $"Scenario exceeded its {timeoutMs} ms watchdog", currentTick());
        }

        await scenario.ConfigureAwait(false); // rethrow scenario exception if any
    }
}
