namespace Atlas.Api;

/// <summary>Thrown when a scenario wait (<c>Ticks</c>/<c>Until</c>/watchdog) exceeds its budget.</summary>
public sealed class ScenarioTimeoutException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScenarioTimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="ticksWaited">The number of ticks that elapsed.</param>
    public ScenarioTimeoutException(string message, int ticksWaited)
        : base(message)
        => TicksWaited = ticksWaited;

    /// <summary>Gets the number of ticks that elapsed before giving up.</summary>
    public int TicksWaited { get; }
}
