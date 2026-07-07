namespace Atlas.Cli;

/// <summary>How a worker process ended, as the orchestrator's process shell observed it.</summary>
/// <param name="ExitCode">The worker's exit code; null when the process was killed on timeout or
/// could not be started at all.</param>
/// <param name="TimedOut">True when the orchestrator killed the worker for exceeding its outer
/// per-class timeout.</param>
/// <param name="Stderr">Everything the worker wrote to stderr (the engine's console chatter and
/// the CLI's own diagnostics land there); crash translation keeps only a tail of it.</param>
internal sealed record WorkerExit(int? ExitCode, bool TimedOut, string Stderr);
