namespace Atlas.Api;

/// <summary>Thrown into a scenario when the embedded server died while the scenario ran.</summary>
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
