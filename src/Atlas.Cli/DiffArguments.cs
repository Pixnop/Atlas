namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas diff` invocation.</summary>
/// <param name="BaselinePath">Path to the baseline TRX report (what the run used to look like).</param>
/// <param name="CandidatePath">Path to the candidate TRX report (what it looks like now).</param>
/// <param name="Json">When true, emit the versioned JSON document instead of the console
/// listing.</param>
/// <param name="JsonTests">When true, add the per-test `tests` array to the JSON document (see
/// docs/specs/2026-07-14-diff-command.md); implies <see cref="EmitJson"/> even when
/// <paramref name="Json"/> itself is false.</param>
internal sealed record DiffArguments(string BaselinePath, string CandidatePath, bool Json = false, bool JsonTests = false)
{
    /// <summary>Gets a value indicating whether the JSON document should be emitted: either
    /// <see cref="Json"/> or <see cref="JsonTests"/> requests it, since --json-tests implies
    /// --json.</summary>
    public bool EmitJson => Json || JsonTests;
}
