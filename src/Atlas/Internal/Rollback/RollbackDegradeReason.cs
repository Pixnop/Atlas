namespace Atlas.Internal.Rollback;

/// <summary>Why a world rollback request could not be honored and degraded to a full host
/// recycle. Structured so callers (per-test degrade messages, strict-isolation failures, the
/// per-class isolation summary) can report the cause instead of a bare "rollback failed".</summary>
internal enum RollbackDegradeReason
{
    /// <summary>The generic bucket: capture or restore threw an exception that none of the more
    /// specific reasons below explains (an engine save-machinery hiccup, an I/O error, an
    /// unexpected engine behavior). The default, so an unclassified failure is never mislabeled
    /// as one of the specific reasons.</summary>
    CaptureOrRestoreFailed = 0,

    /// <summary>Historical (stage 1 only): players were connected when the snapshot was
    /// captured, which stage 1 rollback refused because it did not capture or restore player
    /// state. No longer produced since stage 2 made rollback player-aware; the member is kept so
    /// the reason keeps its meaning wherever it was already recorded (summaries, logs, TRX
    /// output) and so the remaining members keep their numeric values.</summary>
    PlayersJoined,

    /// <summary>Historical (stages 1-2 only): a loaded chunk lived in a mini-dimension
    /// (dimension other than 0), which stage 1 rollback refused because its unload/reload half
    /// covered dimension 0 only. No longer produced since stage 3 made capture and restore
    /// dimension-aware; the member is kept, exactly like <see cref="PlayersJoined"/>, so the
    /// reason keeps its meaning wherever it was already recorded (summaries, logs, TRX output)
    /// and so the remaining members keep their numeric values.</summary>
    MiniDimensionChunksLoaded,

    /// <summary>The boot-validated reflection over the engine internals rollback needs
    /// (<c>ServerMain.chunkThread</c>, <c>ChunkServerThread.gameDatabase</c>, the
    /// mini-dimension discard helper <c>ServerSystemUnloadChunks.TryUnloadChunk</c>) found a
    /// missing or null member: the engine layout drifted in this game version, or the server is
    /// not fully booted, and rollback cannot work at all.</summary>
    EngineDrift,

    /// <summary>A mod's rollback hook handler threw: a listener on one of the cooperation
    /// events (<c>atlas:rollback:captured</c>, <c>atlas:rollback:restored</c>, see
    /// <c>RollbackHooks</c>) raised an exception, so that mod's in-memory state is unknown.
    /// Fail closed: the fallback full recycle rebuilds every mod from scratch, and strict
    /// isolation turns the degrade into a scenario failure carrying the mod's exception.</summary>
    ModHookFailed,
}
