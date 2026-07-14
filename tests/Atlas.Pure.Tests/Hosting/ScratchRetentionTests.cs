using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Contract of the scratch-sweep decision (issue #83): deletion needs every
/// observation to be clean, any single keep reason wins, and the debugging opt-out reads the
/// way an environment variable is actually typed.</summary>
public class ScratchRetentionTests
{
    [Fact]
    public void ShouldDelete_Should_ReturnTrue_When_EveryObservationIsClean()
        => Assert.True(ScratchRetention.ShouldDelete(
            failureObserved: false, hostCrashed: false, teardownJoined: true, keepScratchValue: null));

    [Fact]
    public void ShouldDelete_Should_ReturnFalse_When_TheClassObservedAFailure()
        => Assert.False(ScratchRetention.ShouldDelete(
            failureObserved: true, hostCrashed: false, teardownJoined: true, keepScratchValue: null));

    [Fact]
    public void ShouldDelete_Should_ReturnFalse_When_TheHostCrashed()
        => Assert.False(ScratchRetention.ShouldDelete(
            failureObserved: false, hostCrashed: true, teardownJoined: true, keepScratchValue: null));

    [Fact]
    public void ShouldDelete_Should_ReturnFalse_When_TheGameThreadWasAbandoned()
    {
        // An abandoned game thread may still be running the engine over the scratch path:
        // deleting under it is never safe, green class or not.
        Assert.False(ScratchRetention.ShouldDelete(
            failureObserved: false, hostCrashed: false, teardownJoined: false, keepScratchValue: null));
    }

    [Fact]
    public void ShouldDelete_Should_ReturnFalse_When_TheKeepVariableIsSet()
        => Assert.False(ScratchRetention.ShouldDelete(
            failureObserved: false, hostCrashed: false, teardownJoined: true, keepScratchValue: "1"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData(" 0 ")]
    public void KeepRequested_Should_ReturnFalse_When_TheVariableIsUnsetBlankOrZero(string? value)
        => Assert.False(ScratchRetention.KeepRequested(value));

    [Theory]
    [InlineData("1")]
    [InlineData(" 1 ")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("anything")]
    public void KeepRequested_Should_ReturnTrue_When_TheVariableCarriesAnyOtherValue(string value)
        => Assert.True(ScratchRetention.KeepRequested(value));

    [Fact]
    public void KeepScratchVariable_Should_BeTheDocumentedName_When_Read()
        => Assert.Equal("ATLAS_KEEP_SCRATCH", ScratchRetention.KeepScratchVariable);
}
