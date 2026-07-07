namespace Atlas.Cli;

/// <summary>The parsed arguments of a valid `atlas run` invocation.</summary>
/// <param name="AssemblyPath">Path to the compiled scenario assembly to run or list.</param>
/// <param name="Filter">Optional display-name substring filter; null runs everything.</param>
/// <param name="List">When true, print the discovered scenarios instead of executing them.</param>
/// <param name="Worker">When true, report exclusively as JSONL protocol events on stdout (the
/// stage 2 orchestrator seam).</param>
/// <param name="Classes">Fully qualified scenario class names to run (worker mode only); null
/// runs the whole assembly.</param>
/// <param name="Parallel">When true, orchestrate worker subprocesses over the assembly's
/// scenario classes instead of running them in this process.</param>
/// <param name="ParallelDegree">Explicit worker count for parallel mode; null lets the runner
/// compute the default (half the processor count, capped by the class count).</param>
/// <param name="WorkerTimeoutSeconds">Parallel mode only: outer per-worker timeout in seconds;
/// null uses the runner's default.</param>
/// <param name="TrxPath">Parallel mode only: path of the aggregated TRX report to write; null
/// writes none.</param>
internal sealed record CliArguments(
    string AssemblyPath,
    string? Filter,
    bool List,
    bool Worker = false,
    IReadOnlyList<string>? Classes = null,
    bool Parallel = false,
    int? ParallelDegree = null,
    int? WorkerTimeoutSeconds = null,
    string? TrxPath = null);
