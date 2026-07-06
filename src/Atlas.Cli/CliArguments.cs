namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas run` invocation.</summary>
/// <param name="AssemblyPath">Path to the compiled scenario assembly to run or list.</param>
/// <param name="Filter">Optional display-name substring filter; null runs everything.</param>
/// <param name="List">When true, print the discovered scenarios instead of executing them.</param>
internal sealed record CliArguments(string AssemblyPath, string? Filter, bool List);
