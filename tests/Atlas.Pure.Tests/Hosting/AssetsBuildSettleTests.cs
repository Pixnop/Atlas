using System.Diagnostics;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class AssetsBuildSettleTests
{
    [Fact]
    public void Wait_Should_ReturnTrueWithoutSleeping_When_SignalIsAlreadySettled()
    {
        int samples = 0;

        bool settled = AssetsBuildSettle.Wait(
            () =>
            {
                samples++;
                return true;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(1));

        Assert.True(settled);
        Assert.Equal(1, samples);
    }

    [Fact]
    public void Wait_Should_ReturnTrue_When_SignalSettlesAfterAFewPolls()
    {
        int samples = 0;

        bool settled = AssetsBuildSettle.Wait(
            () => ++samples >= 3,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(1));

        Assert.True(settled);
        Assert.Equal(3, samples);
    }

    [Fact]
    public void Wait_Should_ReturnFalse_When_TheTimeoutElapsesFirst()
    {
        bool settled = AssetsBuildSettle.Wait(
            () => false,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(5));

        Assert.False(settled);
    }

    [Fact]
    public void Wait_Should_SleepBetweenPolls_When_TheSignalIsNotYetSettled()
    {
        int samples = 0;
        var stopwatch = Stopwatch.StartNew();

        bool settled = AssetsBuildSettle.Wait(
            () => ++samples >= 2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(50));

        stopwatch.Stop();
        Assert.True(settled);

        // A missing sleep would busy-loop through the one poll interval in microseconds instead
        // of the configured 50ms; the margin is generous to keep this from flaking.
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(25),
            $"Expected at least one real sleep, only {stopwatch.Elapsed} elapsed.");
    }

    [Fact]
    public void Wait_Should_Throw_When_TheSignalIsNull()
        => Assert.Throws<ArgumentNullException>(() => AssetsBuildSettle.Wait(
            null!, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));
}
