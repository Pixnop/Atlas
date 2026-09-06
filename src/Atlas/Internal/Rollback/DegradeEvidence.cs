namespace Atlas.Internal.Rollback;

/// <summary>Why one world-rollback request degraded to a full host recycle. The reason and the
/// detail are one fact and travel as one value: every surface that reports a degrade (the
/// scenario's isolation report, the strict-isolation failure, the per-class summary's
/// breakdown) needs both, and a request that did not degrade carries neither, so callers hold
/// a <see langword="null"/> evidence instead of a reason nobody may read next to a detail
/// nobody may read.</summary>
/// <param name="Reason">The structured reason, classified by
/// <see cref="RollbackDegrade.Classify"/>.</param>
/// <param name="Detail">The one-line failure detail ("ExceptionType: message").</param>
internal sealed record DegradeEvidence(RollbackDegradeReason Reason, string Detail);
