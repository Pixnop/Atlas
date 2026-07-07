namespace Atlas.Cli;

/// <summary>Run-level metadata of a TRX report (everything the writer needs beyond the outcomes,
/// passed in so the writer stays pure).</summary>
/// <param name="RunName">Human-readable name of the test run.</param>
/// <param name="AssemblyPath">Full path of the scenario assembly (the tests' storage/codeBase).</param>
/// <param name="ComputerName">Machine the run executed on.</param>
/// <param name="Started">When the orchestrated run started (UTC).</param>
/// <param name="Finished">When the orchestrated run finished (UTC).</param>
internal sealed record TrxRunInfo(
    string RunName,
    string AssemblyPath,
    string ComputerName,
    DateTimeOffset Started,
    DateTimeOffset Finished);
