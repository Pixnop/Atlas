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

    [Fact]
    public void DeleteBestEffort_Should_LogOneStderrLineAndSwallow_When_EveryAttemptFails()
    {
        string stderr = CaptureStderr(() => ScratchCleanup.DeleteBestEffort(
            "/scratch/atlas/locked",
            () => throw new IOException("still in use"),
            () => true,
            _ => { }));

        // One line, naming the directory and the exhausted attempt bound; the failure is
        // swallowed (reaching these assertions at all proves nothing escaped).
        Assert.Equal(1, CountOccurrences(stderr, "[Atlas] could not delete the scratch directory"));
        Assert.Contains("'/scratch/atlas/locked'", stderr);
        Assert.Contains($"after {ScratchCleanup.MaxAttempts} attempts", stderr);
        Assert.Contains("leaving it behind", stderr);
    }

    [Fact]
    public void DeleteBestEffort_Should_LogOneStderrLineAndSwallow_When_TheFailureIsNotARetryableRace()
    {
        string stderr = CaptureStderr(() => ScratchCleanup.DeleteBestEffort(
            "/scratch/atlas/odd",
            () => throw new InvalidOperationException("first line" + Environment.NewLine + "second line"),
            () => true,
            _ => { }));

        // The safety-net line carries the exception type and its message flattened to one line,
        // so the evidence survives without a stack trace flooding the output.
        Assert.Equal(1, CountOccurrences(stderr, "[Atlas] could not delete the scratch directory"));
        Assert.Contains("'/scratch/atlas/odd'", stderr);
        Assert.Contains("InvalidOperationException", stderr);
        Assert.Contains("first line second line", stderr);
        Assert.Contains("leaving it behind", stderr);
    }

    /// <summary>Runs <paramref name="action"/> with stderr redirected and returns what it wrote.
    /// Assertions on the result count marker substrings instead of exact equality: other test
    /// classes may run in parallel and write their own stderr lines into the same window.</summary>
    private static string CaptureStderr(Action action)
    {
        var capture = new StringWriter();
        TextWriter real = Console.Error;
        try
        {
            Console.SetError(capture);
            action();
        }
        finally
        {
            Console.SetError(real);
        }

        return capture.ToString();
    }

    private static int CountOccurrences(string text, string marker)
    {
        int count = 0;
        for (int index = text.IndexOf(marker, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
