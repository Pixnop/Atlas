namespace Atlas.Api;

/// <summary>Thrown when Atlas cannot prepare the test environment (install, mods, staging).</summary>
public sealed class AtlasSetupException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AtlasSetupException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public AtlasSetupException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AtlasSetupException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public AtlasSetupException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
