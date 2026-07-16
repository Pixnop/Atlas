namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas stage` invocation.</summary>
/// <param name="TargetPath">The test output to stage: a directory, or a .dll inside one (its
/// directory is used). As given on the command line, not yet resolved or validated.</param>
internal sealed record StageArguments(string TargetPath);
