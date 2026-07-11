namespace Atlas.Cli;

/// <summary>One <c>class-summary</c> protocol event as observed on a worker's stdout: the
/// per-class isolation summary the orchestrator prints live and aggregates into its final
/// summary (and the TRX run-level output).</summary>
/// <param name="ClassName">Fully qualified name of the scenario class.</param>
/// <param name="Summary">The formatted isolation summary line.</param>
internal sealed record WorkerClassSummary(string ClassName, string Summary);
