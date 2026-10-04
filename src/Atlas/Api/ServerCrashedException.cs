namespace Atlas.Api;

/// <summary>Thrown into a scenario when the embedded server died while the scenario ran.</summary>
/// <remarks>Also thrown into every later scenario of a class whose host died or was abandoned
/// after a watchdog timeout, and of a class whose first boot failed (strict boot diagnostics
/// included): the class is not booted again, the message says so and repeats the failure, and for
/// a failed boot the inner exception is that boot's own exception.</remarks>
public sealed class ServerCrashedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ServerCrashedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public ServerCrashedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
