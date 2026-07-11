namespace Atlas.Internal.Rollback;

/// <summary>The outcome of one <c>ServerHost.TryRollbackWorldAsync</c> call: either the world is
/// in the snapshot state, or the attempt degraded and the caller must fall back to a full host
/// recycle, with the structured reason and a one-line detail explaining why.</summary>
internal readonly struct RollbackAttempt
{
    private RollbackAttempt(bool succeeded, RollbackDegradeReason degradeReason, string? degradeDetail)
    {
        Succeeded = succeeded;
        DegradeReason = degradeReason;
        DegradeDetail = degradeDetail;
    }

    /// <summary>Gets a value indicating whether the world is now in the snapshot state (restored,
    /// or captured for the first time).</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the structured reason the attempt degraded; only meaningful when
    /// <see cref="Succeeded"/> is <see langword="false"/>.</summary>
    public RollbackDegradeReason DegradeReason { get; }

    /// <summary>Gets the one-line failure detail ("ExceptionType: message"), or
    /// <see langword="null"/> when the attempt succeeded.</summary>
    public string? DegradeDetail { get; }

    /// <summary>Creates the success outcome.</summary>
    /// <returns>A succeeded attempt.</returns>
    public static RollbackAttempt Success() => new(succeeded: true, default, degradeDetail: null);

    /// <summary>Creates a degraded outcome.</summary>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="detail">The one-line failure detail.</param>
    /// <returns>A degraded attempt.</returns>
    public static RollbackAttempt Degraded(RollbackDegradeReason reason, string detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(detail);
        return new(succeeded: false, reason, detail);
    }
}
