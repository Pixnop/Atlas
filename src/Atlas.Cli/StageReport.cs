namespace Atlas.Cli;

/// <summary>Pure formatting and exit-code core for `atlas stage`: turns already-computed
/// <see cref="StageFileResult"/>s into the printed report and the process exit code. No IO, no
/// VINTAGE_STORY, no EngineStager call here (those stay in <see cref="StageRunner"/> and the
/// core itself); this class only decides what to say about a result it is handed.</summary>
internal static class StageReport
{
    /// <summary>Formats one file's report line, in the same "[Atlas] ..." voice the runtime
    /// staging notice (<c>EngineStaging.DescribeStaged</c>) uses.</summary>
    /// <param name="result">The file's evaluation result.</param>
    /// <returns>The line to print: to the report stream for a non-failure, to the error stream
    /// (already prefixed "atlas: ") for a failure. The caller picks the stream.</returns>
    internal static string Line(StageFileResult result) => result.State switch
    {
        StageFileState.Staged =>
            $"[Atlas] {result.Label}: staged from the VINTAGE_STORY install.",
        StageFileState.AlreadyIdentical =>
            $"[Atlas] {result.Label}: already matches the VINTAGE_STORY install, nothing to do.",
        StageFileState.NothingToStage =>
            $"[Atlas] {result.Label}: no local copy to stage, nothing to do.",
        StageFileState.Failed => $"atlas: {result.FailureMessage}",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.State, "unhandled stage file state"),
    };

    /// <summary>Maps the evaluated results to the process exit code, in the CLI's usage/setup
    /// bucket: 0 when every file either staged cleanly or needed nothing, 2 when any file hit one
    /// of the core's defined failure cases (there is no notion of a "test failure" to report here,
    /// so exit code 1 is never used).</summary>
    /// <param name="results">Every file's evaluation result.</param>
    /// <returns>The exit code.</returns>
    internal static int ExitCode(IEnumerable<StageFileResult> results) =>
        results.Any(result => result.State == StageFileState.Failed) ? 2 : 0;
}
