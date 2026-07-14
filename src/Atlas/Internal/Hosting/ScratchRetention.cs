namespace Atlas.Internal.Hosting;

/// <summary>The pure decision core of the scratch-directory sweep (issue #83): given what the
/// teardown observed, decides whether a disposed host's scratch data path may be deleted. Kept
/// free of IO and environment reads so every keep reason is testable without booting a server
/// (the <see cref="AssetsBuildSettle"/> pattern); the registry's thin shell supplies the
/// observations and performs the actual deletion through <see cref="ScratchCleanup"/>.</summary>
/// <remarks>Background: every embedded host writes its world, logs and staged mods to a fresh
/// directory under the system temp path, and nothing ever deleted them. A day of repeated local
/// runs accumulated 722 directories (1.7 GB) on a 16 GB tmpfs, tripping the ENGINE's own disk
/// guard ("Disk space is below 400 megabytes... Will kill server now"), which then failed every
/// boot with a message that does not point at Atlas at all. The sweep deletes only what has no
/// post-mortem value left: anything red keeps its scratch, because server-main.log in there is
/// the artifact Atlas's own failure messages (see <see cref="EngineStopDetection.Describe"/>)
/// tell users to read.</remarks>
internal static class ScratchRetention
{
    /// <summary>Name of the debugging opt-out variable: when set (to anything but empty or
    /// <c>0</c>), every scratch directory is kept, green classes included.</summary>
    public const string KeepScratchVariable = "ATLAS_KEEP_SCRATCH";

    /// <summary>Decides whether a disposed host's scratch directory may be deleted. Fail safe:
    /// any reason to keep wins, deletion needs every observation to be clean.</summary>
    /// <param name="failureObserved">Whether any scenario of the class owning the host has
    /// failed so far. From the first failure on, every host the class disposes keeps its
    /// scratch: the failure's post-mortem may span hosts (a FreshWorld recycle after a red
    /// scenario, for example).</param>
    /// <param name="hostCrashed">Whether the host recorded a game-thread crash. A crashed
    /// host's scratch is evidence even before the failure lands in any ledger.</param>
    /// <param name="teardownJoined">Whether the dispose-time join of the game thread completed
    /// within its bound. An abandoned game thread may still be running the engine over the
    /// scratch path, so deleting under it is never safe.</param>
    /// <param name="keepScratchValue">The raw value of <see cref="KeepScratchVariable"/>, or
    /// <see langword="null"/> when unset.</param>
    /// <returns><see langword="true"/> when the scratch directory holds no post-mortem value
    /// and may be deleted.</returns>
    public static bool ShouldDelete(
        bool failureObserved,
        bool hostCrashed,
        bool teardownJoined,
        string? keepScratchValue)
        => !failureObserved
           && !hostCrashed
           && teardownJoined
           && !KeepRequested(keepScratchValue);

    /// <summary>Interprets the debugging opt-out: any non-blank value other than <c>0</c>
    /// requests keeping everything, so <c>ATLAS_KEEP_SCRATCH=1</c>, <c>=true</c> and friends
    /// all work, while an explicit <c>=0</c> (or unset) means the sweep runs normally.</summary>
    /// <param name="keepScratchValue">The raw variable value, or <see langword="null"/> when unset.</param>
    /// <returns>Whether the user asked to keep every scratch directory.</returns>
    public static bool KeepRequested(string? keepScratchValue)
        => !string.IsNullOrWhiteSpace(keepScratchValue) && keepScratchValue.Trim() != "0";
}
