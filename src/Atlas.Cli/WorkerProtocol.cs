namespace Atlas.Cli;

/// <summary>Constants of the worker JSONL protocol: the machine-readable seam between a worker
/// process (`atlas run --worker`) and the orchestrator that spawns it (`atlas run --parallel`,
/// <see cref="ParallelRunner"/>). Documented in docs/specs/2026-07-06-worker-protocol.md,
/// where the two halves are the parallel-scenarios design's stages 1 and 2.</summary>
internal static class WorkerProtocol
{
    /// <summary>Protocol version stamped on every emitted event line. Bump when an existing
    /// field changes meaning or disappears; adding fields or event types does not bump it.</summary>
    public const int Version = 1;
}
