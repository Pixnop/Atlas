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
    }

    [Fact]
    public void Compute_Should_ReturnTheSingleValueForEveryStat_When_TheWindowHasOnePass()
    {
        PassTimingStats stats = PassTimingStatistics.Compute([3]);

        Assert.Equal(3, stats.MinMs);
        Assert.Equal(3, stats.MedianMs);
        Assert.Equal(3, stats.P95Ms);
        Assert.Equal(3, stats.MaxMs);
    }

    [Fact]
    public void Compute_Should_ReturnEveryStatAsZero_When_EveryPassWasUnderAMillisecond()
    {
        PassTimingStats stats = PassTimingStatistics.Compute([0, 0, 0]);

        Assert.Equal(0, stats.MinMs);
        Assert.Equal(0, stats.MedianMs);
        Assert.Equal(0, stats.P95Ms);
        Assert.Equal(0, stats.MaxMs);
    }

    [Fact]
    public void Compute_Should_Throw_When_TheWindowIsEmpty()
        => Assert.Throws<ArgumentException>(() => PassTimingStatistics.Compute([]));

    [Fact]
    public void Compute_Should_Throw_When_TheWindowIsNull()
        => Assert.Throws<ArgumentNullException>(() => PassTimingStatistics.Compute(null!));
}
