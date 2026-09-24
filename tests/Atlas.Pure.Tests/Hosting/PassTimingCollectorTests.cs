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

        List<long> a = collector.Start();
        collector.RecordSample(1);

        List<long> b = collector.Start();
        collector.RecordSample(2);

        IReadOnlyList<long> aResult = collector.StopAndCollect(a);
        collector.RecordSample(3);
        IReadOnlyList<long> bResult = collector.StopAndCollect(b);

        Assert.Equal([1L, 2L], aResult);
        Assert.Equal([2L, 3L], bResult);
    }

    [Fact]
    public void StopAndCollect_Should_ReturnAnEmptyWindow_When_NoSampleWasRecorded()
    {
        var collector = new PassTimingCollector();

        List<long> window = collector.Start();

        Assert.Empty(collector.StopAndCollect(window));
    }

    [Fact]
    public void RecordSample_Should_DoNothing_When_NoWindowIsOpen()
    {
        var collector = new PassTimingCollector();

        // No Start() call at all: this is what every pump pass looks like on a host where
        // nothing is calling MeasureTicks. Should not throw, and the sample must not leak into
        // a window opened afterwards.
        collector.RecordSample(5);

        List<long> window = collector.Start();

        Assert.Empty(collector.StopAndCollect(window));
    }
}
