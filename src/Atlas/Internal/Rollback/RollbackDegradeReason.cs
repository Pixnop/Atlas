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

    /// <summary>Players were connected when the snapshot was captured: stage 1 rollback does not
    /// capture or restore player entity state, so the capture refuses to proceed.</summary>
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
