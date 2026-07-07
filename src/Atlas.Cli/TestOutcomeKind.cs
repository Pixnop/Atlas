namespace Atlas.Cli;

/// <summary>How a scenario ended, as aggregated by the orchestrator.</summary>
internal enum TestOutcomeKind
{
    /// <summary>The scenario passed.</summary>
    Passed,

    /// <summary>The scenario failed (including failures the orchestrator synthesized for a
    /// crashed or timed-out worker).</summary>
    Failed,

    /// <summary>The scenario was skipped.</summary>
    Skipped,
}
