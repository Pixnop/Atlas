namespace Atlas.Internal.Rollback;

/// <summary>An in-memory image of the embedded server's world that can be captured once and
/// restored many times, giving <c>RollbackWorld</c> scenarios a clean world without a host
/// recycle. Seam interface: <see cref="WorldSnapshot"/> is the real implementation;
/// <c>ServerHost</c> reaches it through a swappable factory so tests can inject capture or
/// restore failures and exercise the fail-closed fallback to a full host recycle.</summary>
internal interface IWorldSnapshot
{
    /// <summary>Captures the current world state into memory. Runs on the game thread.</summary>
    /// <returns>A task that completes when the snapshot is in memory.</returns>
    Task CaptureAsync();

    /// <summary>Restores the world to the captured state. Runs on the game thread.</summary>
    /// <returns>A task that completes when the world matches the snapshot again.</returns>
    Task RestoreAsync();
}
