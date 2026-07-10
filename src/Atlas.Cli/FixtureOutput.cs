namespace Atlas.Cli;

/// <summary>Overwrite policy of `atlas fixture --out`: never clobber an existing fixture unless
/// the user passed --force. Pure (the file probe is injected), so the policy is unit-testable
/// without a file system.</summary>
internal static class FixtureOutput
{
    /// <summary>Validates the output path against the overwrite policy.</summary>
    /// <param name="outPath">The resolved fixture output path.</param>
    /// <param name="force">Whether --force was given.</param>
    /// <param name="fileExists">File-existence probe, injectable for tests.</param>
    /// <returns>A usage error when the file exists and --force was not given, or null.</returns>
    public static string? Validate(string outPath, bool force, Func<string, bool> fileExists)
        => !force && fileExists(outPath)
            ? $"refusing to overwrite existing fixture '{outPath}' (pass --force to replace it)"
            : null;
}
