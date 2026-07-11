namespace Atlas.Internal.Rollback;

/// <summary>Pure helpers around <see cref="RollbackDegradeReason"/>: classify the exception a
/// capture or restore threw into a structured reason, and describe a reason as the short human
/// phrase every degrade surface (per-test output, strict-isolation failures, stderr warnings,
/// the per-class isolation summary) embeds, so the wording is consistent everywhere.</summary>
internal static class RollbackDegrade
{
    /// <summary>Classifies the exception a world rollback attempt threw.</summary>
    /// <param name="exception">The exception caught by the fail-closed fallback.</param>
    /// <returns>The structured reason: the one carried by a
    /// <see cref="RollbackUnsupportedException"/>, or the generic
    /// <see cref="RollbackDegradeReason.CaptureOrRestoreFailed"/> bucket for anything else.</returns>
    public static RollbackDegradeReason Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is RollbackUnsupportedException unsupported
            ? unsupported.Reason
            : RollbackDegradeReason.CaptureOrRestoreFailed;
    }

    /// <summary>Describes a degrade reason as a short human phrase (e.g. "mini-dimension chunks
    /// loaded"), suitable after "Reason:" or inside a summary breakdown.</summary>
    /// <param name="reason">The structured reason.</param>
    /// <returns>The phrase.</returns>
    public static string Describe(RollbackDegradeReason reason) => reason switch
    {
        RollbackDegradeReason.PlayersJoined => "players joined",
        RollbackDegradeReason.MiniDimensionChunksLoaded => "mini-dimension chunks loaded",
        RollbackDegradeReason.EngineDrift => "engine internals drifted",
        _ => "capture or restore failed",
    };
}
