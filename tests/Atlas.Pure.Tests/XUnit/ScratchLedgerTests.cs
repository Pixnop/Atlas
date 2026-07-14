using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

/// <summary>Contract of the failure record feeding the scratch sweep (issue #83): failures are
/// per class, sticky for the process lifetime, and unknown classes are clean. The ledger is
/// process-wide static state, so every test uses its own private marker type.</summary>
public class ScratchLedgerTests
{
    [Fact]
    public void HasObservedFailure_Should_ReturnFalse_When_TheClassNeverFailed()
        => Assert.False(ScratchLedger.HasObservedFailure(typeof(NeverFailedMarker)));

    [Fact]
    public void HasObservedFailure_Should_ReturnTrue_When_AFailureWasRecorded()
    {
        ScratchLedger.RecordFailure(typeof(FailedOnceMarker));

        Assert.True(ScratchLedger.HasObservedFailure(typeof(FailedOnceMarker)));
    }

    [Fact]
    public void HasObservedFailure_Should_StayTrue_When_TheSameFailureIsRecordedTwice()
    {
        ScratchLedger.RecordFailure(typeof(FailedTwiceMarker));
        ScratchLedger.RecordFailure(typeof(FailedTwiceMarker));

        Assert.True(ScratchLedger.HasObservedFailure(typeof(FailedTwiceMarker)));
    }

    [Fact]
    public void RecordFailure_Should_NotLeakAcrossClasses_When_AnotherClassFails()
    {
        ScratchLedger.RecordFailure(typeof(RedNeighborMarker));

        Assert.False(ScratchLedger.HasObservedFailure(typeof(GreenNeighborMarker)));
    }

    [Fact]
    public void RecordFailure_Should_Throw_When_TheClassIsNull()
        => Assert.Throws<ArgumentNullException>(() => ScratchLedger.RecordFailure(null!));

    [Fact]
    public void HasObservedFailure_Should_Throw_When_TheClassIsNull()
        => Assert.Throws<ArgumentNullException>(() => ScratchLedger.HasObservedFailure(null!));

    private sealed class NeverFailedMarker
    {
    }

    private sealed class FailedOnceMarker
    {
    }

    private sealed class FailedTwiceMarker
    {
    }

    private sealed class RedNeighborMarker
    {
    }

    private sealed class GreenNeighborMarker
    {
    }
}
