namespace Atlas.Cli;

/// <summary>Constants of the worker JSONL protocol: the machine-readable seam between a worker
/// process (`atlas run --worker`, stage 1 of the parallel-scenarios design) and the future
/// orchestrator (stage 2). Documented in docs/specs/2026-07-06-worker-protocol.md.</summary>
internal static class WorkerProtocol
{
    /// <summary>Protocol version stamped on every emitted event line. Bump when an existing
    /// field changes meaning or disappears; adding fields or event types does not bump it.</summary>
    public const int Version = 1;
}
