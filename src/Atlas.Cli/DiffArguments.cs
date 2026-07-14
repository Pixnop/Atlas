namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas diff` invocation.</summary>
/// <param name="BaselinePath">Path to the baseline TRX report (what the run used to look like).</param>
/// <param name="CandidatePath">Path to the candidate TRX report (what it looks like now).</param>
/// <param name="Json">When true, emit the versioned JSON document instead of the console
/// listing.</param>
internal sealed record DiffArguments(string BaselinePath, string CandidatePath, bool Json = false);
