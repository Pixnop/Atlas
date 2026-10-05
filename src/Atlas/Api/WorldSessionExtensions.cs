using Atlas.Internal.Scheduling;

namespace Atlas.Api;

/// <summary>Helpers over <see cref="IWorldSession"/> that are not part of its interface, so a
/// consumer that implements the interface does not have to carry them.</summary>
public static class WorldSessionExtensions
{
    /// <summary>Waits until a predicate becomes true, polled once per tick, like
    /// <see cref="IWorldSession.Until"/>, and says what it was waiting for when the bound elapses:
    /// the timeout message carries <paramref name="description"/>, so a failure in a scenario with
    /// several waits names the one that gave up instead of only its tick count.</summary>
    /// <param name="world">The session.</param>
    /// <param name="predicate">The condition to poll, with the semantics of
    /// <see cref="IWorldSession.Until"/>: first evaluated on the tick after the call, on the game
    /// thread.</param>
    /// <param name="description">What the wait is for, in the words of the condition (<c>"the zombie
    /// has despawned"</c>); it appears in quotes in the timeout message.</param>
    /// <param name="timeoutTicks">The maximum number of ticks to wait before giving up. Must be
    /// at least 1; the default is the one <see cref="IWorldSession.Until"/> has.</param>
    /// <returns>A task that completes when <paramref name="predicate"/> is true.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="world"/>,
    /// <paramref name="predicate"/> or <paramref name="description"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="description"/> is empty or
    /// only white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeoutTicks"/>
    /// is less than 1.</exception>
    /// <exception cref="ScenarioTimeoutException">Thrown when <paramref name="timeoutTicks"/> elapses
    /// without <paramref name="predicate"/> becoming true. Its message is the one
    /// <see cref="IWorldSession.Until"/> gives with the description added
    /// (<c>Until predicate "the zombie has despawned" still false after 600 ticks (timeoutTicks is
    /// 600; pass a larger value to wait longer)</c>), and <see cref="ScenarioTimeoutException.TicksWaited"/>
    /// is the same.</exception>
    /// <remarks><para>An extension method, not a member of <see cref="IWorldSession"/>, so the
    /// interface is unchanged. A call with a string as its second argument reaches this method; a
    /// call with a tick count there (<c>Until(predicate, 100)</c>, <c>Until(predicate,
    /// timeoutTicks: 100)</c>) or nothing reaches the interface's own, which keeps its message.</para>
    /// <para>Only the wait's own timeout is reworded. A <see cref="ScenarioTimeoutException"/> the
    /// predicate raises from some other wait, and any other exception it throws, pass through
    /// unchanged. The rethrown exception is a new one, so its stack trace starts here.</para></remarks>
    // The default is the shared bound, not a literal, like the interface's own: it cannot drift
    // from IWorldSession.Until, and a const default is baked into this signature's metadata as 600.
    public static Task Until(
        this IWorldSession world, Func<bool> predicate, string description, int timeoutTicks = TickBounds.DefaultWait)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutTicks, 1);
        return UntilDescribed(world, predicate, description, timeoutTicks);
    }

    // Split off so the checks above throw synchronously, like IWorldSession.Until's do, instead of
    // faulting the task.
    private static async Task UntilDescribed(IWorldSession world, Func<bool> predicate, string description, int timeoutTicks)
    {
        try
        {
            await world.Until(predicate, timeoutTicks).ConfigureAwait(true);
        }
        catch (ScenarioTimeoutException timeout) when (timeout.TicksWaited == timeoutTicks)
        {
            // The wait's own timeout is the only one that counts exactly timeoutTicks: a timeout the
            // predicate raised from another wait is that wait's, and keeps its own words.
            throw new ScenarioTimeoutException(
                TickSource.UntilTimeoutMessage(timeout.TicksWaited, timeoutTicks, description), timeout.TicksWaited);
        }
    }
}
