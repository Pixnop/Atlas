namespace Atlas.Internal.Hosting;

/// <summary>Best-effort recursive deletion of a host's scratch data directory, with a short
/// bounded retry: the engine may hold file handles (log writers, the savegame database) for a
/// beat after the game thread exits, and the first delete attempt can lose that race. The retry
/// loop is a pure core with injected delegates (the <see cref="AssetsBuildSettle"/> /
/// <c>RunnerDisposal</c> pattern) so its bounds are testable without touching the filesystem;
/// <see cref="DeleteBestEffort"/> is the thin IO shell. A deletion that still fails after the
/// retries is logged as one stderr line and otherwise swallowed: a leftover scratch directory
/// is untidy, failing a green test run over it would be absurd.</summary>
internal static class ScratchCleanup
{
    /// <summary>Total delete attempts before giving up and leaving the directory behind.</summary>
    internal const int MaxAttempts = 3;

    /// <summary>Pause between two attempts, sized for the engine's handle-release tail.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Deletes <paramref name="path"/> recursively, best-effort: retries a losing race
    /// with the engine's file handles a bounded number of times, then logs one stderr line and
    /// gives up. Never throws.</summary>
    /// <param name="path">The scratch directory to delete.</param>
    public static void DeleteBestEffort(string path)
    {
        try
        {
            if (!TryDelete(() => Directory.Delete(path, recursive: true), () => Directory.Exists(path), Thread.Sleep))
            {
                Console.Error.WriteLine(
                    $"[Atlas] could not delete the scratch directory '{path}' after {MaxAttempts} attempts; " +
                    "leaving it behind.");
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad: the sweep is teardown hygiene and must never surface as a test
            // failure, whatever the filesystem throws (the retried IO exceptions are handled in
            // the core; this is the safety net for everything else).
            Console.Error.WriteLine(
                $"[Atlas] could not delete the scratch directory '{path}' " +
                $"({ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}); leaving it behind.");
        }
    }

    /// <summary>Pure core of <see cref="DeleteBestEffort"/>: tries <paramref name="deleteRecursive"/>
    /// up to <see cref="MaxAttempts"/> times, sleeping <see cref="RetryDelay"/> between attempts,
    /// retrying only the failure shapes a handle race produces.</summary>
    /// <param name="deleteRecursive">Deletes the directory tree; may throw on a lost race.</param>
    /// <param name="exists">Reads whether the directory still exists; a vanished directory is
    /// success (someone else finished the job).</param>
    /// <param name="sleep">Sleeps for the given duration (injectable for tests).</param>
    /// <returns><see langword="true"/> when the directory is gone; <see langword="false"/> when
    /// every attempt failed and the caller decides how loudly to give up.</returns>
    internal static bool TryDelete(Action deleteRecursive, Func<bool> exists, Action<TimeSpan> sleep)
    {
        for (int remaining = MaxAttempts - 1; ; remaining--)
        {
            try
            {
                if (!exists())
                {
                    return true;
                }

                deleteRecursive();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (remaining <= 0)
                {
                    return false;
                }

                sleep(RetryDelay);
            }
        }
    }
}
