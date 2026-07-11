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

    /// <summary>A loaded chunk lives in a mini-dimension (dimension other than 0): stage 1
    /// rollback covers dimension 0 only (the spec defers dimensions to stage 3). The classic
    /// trigger is a fixture pregenerating mini-dimensions at boot, which poisons every rollback
    /// of the class.</summary>
    MiniDimensionChunksLoaded,

    /// <summary>The boot-validated reflection over the engine internals rollback needs
    /// (<c>ServerMain.chunkThread</c>, <c>ChunkServerThread.gameDatabase</c>) found a missing or
    /// null field: the engine layout drifted in this game version, or the server is not fully
    /// booted, and rollback cannot work at all.</summary>
    EngineDrift,
}
