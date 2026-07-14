using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class DurationShiftRuleTests
{
    [Fact]
    public void Evaluate_Should_ReportASlowerShift_When_BothThresholdsAreMet()
    {
        DurationShift? shift = DurationShiftRule.Evaluate("Ns.A.T", 400, 900);

        Assert.Equal(new DurationShift("Ns.A.T", 400, 900, Slower: true), shift);
    }

    [Fact]
    public void Evaluate_Should_ReportAFasterShift_When_TheCandidateSpedUp()
    {
        DurationShift? shift = DurationShiftRule.Evaluate("Ns.A.T", 2000, 600);

        Assert.Equal(new DurationShift("Ns.A.T", 2000, 600, Slower: false), shift);
    }

    [Fact]
    public void Evaluate_Should_ReportNothing_When_TheAbsoluteShiftIsBelowTheFloor()
    {
        // 3x slower, but only 400 ms apart: noise territory for a fast test.
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 200, 600));
    }

    [Fact]
    public void Evaluate_Should_ReportNothing_When_TheFactorIsBelowTwo()
    {
        // 600 ms apart, but only 1.6x: a big test drifting, not a shift.
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 1000, 1600));
    }

    [Fact]
    public void Evaluate_Should_ReportTheShift_When_BothThresholdsAreExactlyMet()
    {
        Assert.NotNull(DurationShiftRule.Evaluate("Ns.A.T", 500, 1000));
    }

    [Fact]
    public void Evaluate_Should_ReportNothing_When_EitherThresholdIsOneMillisecondShort()
    {
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 499, 998));
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 501, 1001));
    }

    [Fact]
    public void Evaluate_Should_ReportTheShift_When_TheFasterSideIsZero()
    {
        // A 0 ms baseline satisfies the factor trivially; the absolute floor is the gate.
        Assert.NotNull(DurationShiftRule.Evaluate("Ns.A.T", 0, 500));
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 0, 499));
    }

    [Fact]
    public void Evaluate_Should_ReportNothing_When_TheDurationsAreEqual()
    {
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", 5000, 5000));
    }

    [Theory]
    [InlineData(null, 900L)]
    [InlineData(900L, null)]
    [InlineData(null, null)]
    public void Evaluate_Should_ReportNothing_When_EitherDurationIsMissing(long? baseline, long? candidate)
    {
        Assert.Null(DurationShiftRule.Evaluate("Ns.A.T", baseline, candidate));
    }
}
