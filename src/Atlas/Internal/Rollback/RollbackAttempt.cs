namespace Atlas.Internal.Rollback;

/// <summary>The outcome of one <c>ServerHost.TryRollbackWorldAsync</c> call: either the world is
/// in the snapshot state, or the attempt degraded and the caller must fall back to a full host
/// recycle, carrying the <see cref="DegradeEvidence"/> that says why.</summary>
internal readonly struct RollbackAttempt
{
    private RollbackAttempt(bool captured, DegradeEvidence? degrade)
    {
        Captured = captured;
        Degrade = degrade;
    }

    /// <summary>Gets a value indicating whether the world is now in the snapshot state (restored,
    /// or captured for the first time), which is exactly "no degrade evidence".</summary>
    public bool Succeeded => Degrade is null;

    /// <summary>Gets a value indicating whether this successful attempt CAPTURED the snapshot
    /// instead of restoring it (the lazy first request, or the first request after a degrade
    /// discarded the snapshot). Lets the caller tally captures separately from restores, so the
    /// class summary's arithmetic is self-explanatory (issue #71); a degraded attempt captured
    /// nothing, so it is <see langword="false"/> there.</summary>
    public bool Captured { get; }

    /// <summary>Gets why the attempt degraded, or <see langword="null"/> when it succeeded.</summary>
    public DegradeEvidence? Degrade { get; }

    /// <summary>Creates the success outcome.</summary>
    /// <param name="captured">Whether the attempt captured the snapshot instead of restoring it.</param>
    /// <returns>A succeeded attempt.</returns>
    public static RollbackAttempt Success(bool captured) => new(captured, degrade: null);

    /// <summary>Creates a degraded outcome.</summary>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="detail">The one-line failure detail.</param>
    /// <returns>A degraded attempt.</returns>
    public static RollbackAttempt Degraded(RollbackDegradeReason reason, string detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(detail);
        return new(captured: false, new DegradeEvidence(reason, detail));
    }
}
