using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class PassTimingCollectorTests
{
    [Fact]
    public void RecordSample_Should_AppendToEveryOpenWindow_When_TwoWindowsOverlap()
    {
        // Mirrors two overlapping MeasureTicks calls on the same host: window "a" opens first,
        // window "b" opens while "a" is still collecting, and each should see exactly the
        // samples recorded while it, specifically, was open - not the other window's samples,
        // and not an empty result from a second Start() silently discarding the first.
        var collector = new PassTimingCollector();

        PassWindow a = collector.Start();
        collector.RecordSample(1, 1_100.0);

        PassWindow b = collector.Start();
        collector.RecordSample(2, 2_200.0);

        PassWindow aResult = collector.StopAndCollect(a);
        collector.RecordSample(3, 3_300.0);
        PassWindow bResult = collector.StopAndCollect(b);

        Assert.Equal([1L, 2L], aResult.BusyMs);
        Assert.Equal([1_100.0, 2_200.0], aResult.BusyMicroseconds);
        Assert.Equal([2L, 3L], bResult.BusyMs);
        Assert.Equal([2_200.0, 3_300.0], bResult.BusyMicroseconds);
    }

    [Fact]
    public void StopAndCollect_Should_ReturnAnEmptyWindow_When_NoSampleWasRecorded()
    {
        var collector = new PassTimingCollector();

        PassWindow window = collector.Start();

        PassWindow collected = collector.StopAndCollect(window);
        Assert.Empty(collected.BusyMs);
        Assert.Empty(collected.BusyMicroseconds);
    }

    [Fact]
    public void RecordSample_Should_DoNothing_When_NoWindowIsOpen()
    {
        var collector = new PassTimingCollector();

        // No Start() call at all: this is what every pump pass looks like on a host where
        // nothing is calling MeasureTicks. Should not throw, and the sample must not leak into
        // a window opened afterwards.
        collector.RecordSample(5, 5_500.0);

        PassWindow window = collector.Start();

        PassWindow collected = collector.StopAndCollect(window);
        Assert.Empty(collected.BusyMs);
        Assert.Empty(collected.BusyMicroseconds);
    }
}
