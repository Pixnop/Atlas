namespace Atlas.Cli;

/// <summary>What `atlas stage` did (or could not do) for one staged file or file pair.</summary>
internal enum StageFileState
{
    /// <summary>The file was rewritten from the VINTAGE_STORY install.</summary>
    Staged,

    /// <summary>A local copy existed and already matched the install's; nothing to change.</summary>
    AlreadyIdentical,

    /// <summary>No local copy shadows probing (or the install ships none to compare against);
    /// nothing to change.</summary>
    NothingToStage,

    /// <summary>Staging hit one of the core's defined failure cases; see
    /// <see cref="StageFileResult.FailureMessage"/> for the actionable message.</summary>
    Failed,
}

/// <summary>One line of the `atlas stage` report.</summary>
/// <param name="Label">What was evaluated (e.g. "VintagestoryAPI.dll and VintagestoryAPI.pdb").</param>
/// <param name="State">The outcome.</param>
/// <param name="FailureMessage">The actionable error EngineStaging formatted for the failure
/// case (unwritable output, install without its pdb, a diverged copy already bound, the
/// Newtonsoft direction refusal); set only when <paramref name="State"/> is
/// <see cref="StageFileState.Failed"/>.</param>
internal sealed record StageFileResult(string Label, StageFileState State, string? FailureMessage = null);
