namespace Atlas.Internal.Rollback;

/// <summary>Thrown by the world snapshot machinery when a capture or restore hits a condition
/// rollback is known not to support (mini-dimension chunks, engine layout drift), carrying the
/// structured <see cref="Reason"/> so the fail-closed fallback in
/// <c>ServerHost.TryRollbackWorldAsync</c> can report WHY it degraded instead of a bare
/// exception message. Never escapes to scenario authors through the fallback path: it is
/// caught, classified and turned into a degrade result; only tests driving
/// <see cref="WorldSnapshot"/> directly observe it.</summary>
internal sealed class RollbackUnsupportedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RollbackUnsupportedException"/> class.</summary>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="reason">The structured degrade reason.</param>
    public RollbackUnsupportedException(string message, RollbackDegradeReason reason)
        : base(message)
        => Reason = reason;

    /// <summary>Initializes a new instance of the <see cref="RollbackUnsupportedException"/>
    /// class wrapping a causing exception (e.g. the exception a mod's rollback hook handler
    /// threw, classified as <see cref="RollbackDegradeReason.ModHookFailed"/>).</summary>
    /// <param name="message">The human-readable explanation; should embed the inner exception's
    /// type and message, because only this message reaches the one-line degrade detail.</param>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="innerException">The causing exception.</param>
    public RollbackUnsupportedException(string message, RollbackDegradeReason reason, Exception innerException)
        : base(message, innerException)
        => Reason = reason;

    /// <summary>Gets the structured reason rollback cannot proceed.</summary>
    public RollbackDegradeReason Reason { get; }
}
