namespace Atlas.Api;

/// <summary>Thrown when a scenario requested strict world isolation
/// (<c>[AtlasScenario(RollbackWorld = true, StrictIsolation = true)]</c>) and its rollback
/// request degraded to a full host recycle instead. The message carries the structured degrade
/// reason (mini-dimension chunks loaded, engine drift, or a generic capture/restore failure),
/// so a suite that treats the rollback speedup as a contract fails
/// loudly with the cause instead of silently paying full recycles. A genuine server crash is
/// never re-labelled as this exception: crash paths keep surfacing as
/// <see cref="ServerCrashedException"/>.</summary>
public sealed class AtlasIsolationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AtlasIsolationException"/> class.</summary>
    /// <param name="message">The failure message, carrying the degrade reason.</param>
    public AtlasIsolationException(string message)
        : base(message)
    {
    }
}
