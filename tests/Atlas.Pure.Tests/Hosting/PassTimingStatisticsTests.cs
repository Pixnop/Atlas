using Atlas.Api;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class PassTimingStatisticsTests
{
    [Theory]
    [InlineData(10, 0, 9)] // cursor at 0 (just wrapped): the last write landed in the final slot
    [InlineData(10, 1, 0)] // cursor at 1: the last write landed in slot 0
    [InlineData(10, 7, 6)]
    [InlineData(1, 0, 0)] // a single-slot buffer always points at itself
    [InlineData(10, -3, 6)] // a cursor outside [0, length) still folds into range
    public void LastWrittenIndex_Should_PointAtTheSlotBeforeTheCursor_When_TheBufferWraps(
        int length, int cursor, int expected)
        => Assert.Equal(expected, PassTimingStatistics.LastWrittenIndex(length, cursor));

    [Fact]
    public void LastWrittenIndex_Should_Throw_When_TheLengthIsNotPositive()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PassTimingStatistics.LastWrittenIndex(0, 0));

    [Fact]
    public void Compute_Should_ReportEveryStat_When_GivenAMixedWindow()
    {
        // 10 samples, chosen so the nearest-rank percentiles land on distinct, checkable values:
        // sorted this is 0,1,2,3,4,5,6,7,8,40 - median is the 5th (index 4) = 4, p95 is the 10th
        // (index 9, ceil(0.95*10)=10) = 40.
        long[] samples = [8, 3, 40, 0, 6, 1, 7, 4, 2, 5];

        PassTimingStats stats = PassTimingStatistics.Compute(samples);

        Assert.Equal(0, stats.MinMs);
        Assert.Equal(4, stats.MedianMs);
        Assert.Equal(40, stats.P95Ms);
        Assert.Equal(40, stats.MaxMs);

        // The same window summed: 8+3+40+0+6+1+7+4+2+5 = 76, over 10 passes.
        Assert.Equal(76, stats.TotalMs);
        Assert.Equal(7.6, stats.MeanMs, precision: 10);
    }

    [Fact]
    public void Compute_Should_ReportALowerP95ThanMax_When_TheWindowHasTwentySamples()
    {
        // 20 samples 1..20: nearest-rank p95 is ceil(0.95*20)=19th value (index 18) = 19, one
        // below the max of 20 - pins that p95 is not just an alias for max once the window is
        // big enough to tell them apart (it collapses onto max at n=10, the only size the other
        // Compute tests use).
        long[] samples = [.. Enumerable.Range(1, 20).Select(i => (long)i)];

        PassTimingStats stats = PassTimingStatistics.Compute(samples);

        Assert.Equal(19, stats.P95Ms);
        Assert.Equal(20, stats.MaxMs);
    }

    [Fact]
    public void Compute_Should_ReturnTheLowerValue_When_TheWindowHasAnEvenCountOfSamples()
    {
        // Nearest-rank median for an even count is the lower of the two middle values, never an
        // average: rank = ceil(0.5*2) = 1, index 0.
        PassTimingStats stats = PassTimingStatistics.Compute([1, 3]);

        Assert.Equal(1, stats.MedianMs);
    }

    [Fact]
    public void Compute_Should_ReturnTheSingleValueForEveryStat_When_TheWindowHasOnePass()
    {
        PassTimingStats stats = PassTimingStatistics.Compute([3]);

        Assert.Equal(3, stats.MinMs);
        Assert.Equal(3, stats.MedianMs);
        Assert.Equal(3, stats.P95Ms);
        Assert.Equal(3, stats.MaxMs);
        Assert.Equal(3, stats.TotalMs);
        Assert.Equal(3.0, stats.MeanMs);
    }

    [Fact]
    public void Compute_Should_ReturnEveryStatAsZero_When_EveryPassWasUnderAMillisecond()
    {
        PassTimingStats stats = PassTimingStatistics.Compute([0, 0, 0]);

        Assert.Equal(0, stats.MinMs);
        Assert.Equal(0, stats.MedianMs);
        Assert.Equal(0, stats.P95Ms);
        Assert.Equal(0, stats.MaxMs);
        Assert.Equal(0, stats.TotalMs);
        Assert.Equal(0.0, stats.MeanMs);
    }

    [Fact]
    public void Compute_Should_AverageTheSamples_Not_PickOne_When_TheWindowHasAnEvenCountOfSamples()
    {
        // The median of [1, 3] is the lower middle value (1, see the test above); the mean is a
        // real average of the two, so unlike the order statistics it need not be a sampled value.
        PassTimingStats stats = PassTimingStatistics.Compute([1, 3]);

        Assert.Equal(4, stats.TotalMs);
        Assert.Equal(2.0, stats.MeanMs);
    }

    [Fact]
    public void Compute_Should_KeepTheFraction_When_TheTotalDoesNotDivideEvenly()
    {
        // Two 1ms passes and one 2ms pass: every sample is whole, the mean is not.
        PassTimingStats stats = PassTimingStatistics.Compute([1, 1, 2]);

        Assert.Equal(4, stats.TotalMs);
        Assert.Equal(4.0 / 3.0, stats.MeanMs, precision: 10);
    }

    [Fact]
    public void Compute_Should_Throw_When_TheWindowIsEmpty()
        => Assert.Throws<ArgumentException>(() => PassTimingStatistics.Compute([]));

    [Fact]
    public void Compute_Should_Throw_When_TheWindowIsNull()
        => Assert.Throws<ArgumentNullException>(() => PassTimingStatistics.Compute(null!));

    [Theory]
    [InlineData(33_150, 0, 33.333332f, 150)] // idle pass: the engine floors it to 0 ms and asked for a 33 ms sleep
    [InlineData(33_100, 20, 33.333332f, 20_100)] // the engine slept 13 ms of the 33.3 budget, so 13 ms come off
    [InlineData(40_300, 40, 33.333332f, 40_300)] // over budget: no sleep was asked for, the wall time is the busy time
    [InlineData(34_500, 1, 33.333332f, 2_000)] // a sleep that overshot by 2.5 ms is held at the top of the engine's millisecond
    [InlineData(32_000, 3, 33.333332f, 3_000)] // a sleep that came back early reads below the engine's sample: held at its floor
    [InlineData(10_050, 0, 10.0f, 50)] // a different TickTime: the sleep is the whole milliseconds the engine computed
    public void BusyMicroseconds_Should_TakeTheRequestedSleepOffTheWallTime_And_StayInsideTheEnginesMillisecond(
        double wallMicroseconds, long engineBusyMs, float tickTimeMs, double expected)
        => Assert.Equal(expected, PassTimingStatistics.BusyMicroseconds(wallMicroseconds, engineBusyMs, tickTimeMs), precision: 6);

    [Fact]
    public void BusyMicroseconds_Should_FloorTheRequestedSleepToWholeMilliseconds_Like_TheEngine()
    {
        // The engine sleeps (int)Math.Max(0f, TickTime - ms): 33.333332 - 1 = 32.333332 asks for 32 ms,
        // so a 1 ms pass that overslept by 50 microseconds took 33 ms and 50 microseconds from outside.
        // The result is 1_050: the 33_050 wall time less 32_000, inside [1_000, 2_000].
        Assert.Equal(1_050, PassTimingStatistics.BusyMicroseconds(33_050, 1, 33.333332f), precision: 6);
    }

    [Fact]
    public void Compute_Should_AverageTheMicrosecondSamples_When_GivenBothFigures()
    {
        // The millisecond figures are the engine's floors (0, 0, 1); the stopwatch's sit inside them.
        PassTimingStats stats = PassTimingStatistics.Compute([0, 0, 1], [150.0, 420.0, 1_300.0]);

        Assert.Equal((150.0 + 420.0 + 1_300.0) / 3, stats.MeanMicroseconds, precision: 10);

        // And leaves every millisecond figure as the single-argument overload computes it.
        PassTimingStats millisecondsOnly = PassTimingStatistics.Compute([0, 0, 1]);
        Assert.Equal(millisecondsOnly.MinMs, stats.MinMs);
        Assert.Equal(millisecondsOnly.MedianMs, stats.MedianMs);
        Assert.Equal(millisecondsOnly.P95Ms, stats.P95Ms);
        Assert.Equal(millisecondsOnly.MaxMs, stats.MaxMs);
        Assert.Equal(millisecondsOnly.TotalMs, stats.TotalMs);
        Assert.Equal(millisecondsOnly.MeanMs, stats.MeanMs);
    }

    [Fact]
    public void Compute_Should_Throw_When_TheTwoWindowsDifferInLength()
        => Assert.Throws<ArgumentException>(() => PassTimingStatistics.Compute([1, 2], [100.0]));

    [Fact]
    public void Compute_Should_Throw_When_TheMicrosecondWindowIsNull()
        => Assert.Throws<ArgumentNullException>(() => PassTimingStatistics.Compute([1], null!));
}
