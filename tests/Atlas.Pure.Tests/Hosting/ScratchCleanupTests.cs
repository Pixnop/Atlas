using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Contract of the best-effort scratch deletion (issue #83): the retry loop is
/// bounded, retries only the failure shapes a handle race produces, treats a vanished
/// directory as success, and the IO shell never throws (it logs one stderr line instead).</summary>
public class ScratchCleanupTests
{
    [Fact]
    public void TryDelete_Should_DeleteOnceWithoutSleeping_When_TheFirstAttemptSucceeds()
    {
        int deletes = 0;
        var sleeps = new List<TimeSpan>();

        bool result = ScratchCleanup.TryDelete(() => deletes++, () => true, sleeps.Add);

        Assert.True(result);
        Assert.Equal(1, deletes);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void TryDelete_Should_ReportSuccessWithoutDeleting_When_TheDirectoryIsAlreadyGone()
    {
        int deletes = 0;

        bool result = ScratchCleanup.TryDelete(() => deletes++, () => false, _ => { });

        Assert.True(result);
        Assert.Equal(0, deletes);
    }

    [Fact]
    public void TryDelete_Should_RetryAfterTheDelay_When_TheFirstAttemptLosesTheHandleRace()
    {
        int attempts = 0;
        var sleeps = new List<TimeSpan>();

        bool result = ScratchCleanup.TryDelete(
            () =>
            {
                if (++attempts == 1)
                {
                    throw new IOException("file in use");
                }
            },
            () => true,
            sleeps.Add);

        Assert.True(result);
        Assert.Equal(2, attempts);
        Assert.Equal([ScratchCleanup.RetryDelay], sleeps);
    }

    [Fact]
    public void TryDelete_Should_GiveUpAfterTheAttemptBound_When_EveryAttemptFails()
    {
        int attempts = 0;
        var sleeps = new List<TimeSpan>();

        bool result = ScratchCleanup.TryDelete(
            () =>
            {
                attempts++;
                throw new UnauthorizedAccessException("still locked");
            },
            () => true,
            sleeps.Add);

        Assert.False(result);
        Assert.Equal(ScratchCleanup.MaxAttempts, attempts);
        Assert.Equal(ScratchCleanup.MaxAttempts - 1, sleeps.Count);
        Assert.All(sleeps, s => Assert.Equal(ScratchCleanup.RetryDelay, s));
    }

    [Fact]
    public void TryDelete_Should_TreatTheVanishedDirectoryAsSuccess_When_ARetryFindsItGone()
    {
        // First attempt loses the race, and by the second attempt the directory is gone
        // (the engine's own teardown finished the job): that is success, not a retry failure.
        bool gone = false;

        bool result = ScratchCleanup.TryDelete(
            () =>
            {
                gone = true;
                throw new IOException("lost the race");
            },
            () => !gone,
            _ => { });

        Assert.True(result);
    }

    [Fact]
    public void TryDelete_Should_PropagateToTheShell_When_TheFailureIsNotARetryableRace()
        => Assert.Throws<InvalidOperationException>(() => ScratchCleanup.TryDelete(
            () => throw new InvalidOperationException("not a handle race"),
            () => true,
            _ => { }));

    [Fact]
    public void DeleteBestEffort_Should_DeleteTheTreeRecursively_When_ThePathExists()
    {
        string root = Directory.CreateTempSubdirectory("atlas-scratchcleanup-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        File.WriteAllText(Path.Combine(root, "Logs", "server-main.log"), "log");

        ScratchCleanup.DeleteBestEffort(root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DeleteBestEffort_Should_DoNothingQuietly_When_ThePathDoesNotExist()
    {
        string ghost = Path.Combine(Path.GetTempPath(), $"atlas-scratchcleanup-ghost-{Guid.NewGuid():N}");

        ScratchCleanup.DeleteBestEffort(ghost);

        Assert.False(Directory.Exists(ghost));
    }
}
