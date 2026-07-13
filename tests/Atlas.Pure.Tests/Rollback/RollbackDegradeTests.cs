using Atlas.Internal.Rollback;

namespace Atlas.Pure.Tests.Rollback;

public class RollbackDegradeTests
{
    [Fact]
    public void Classify_Should_ReturnCarriedReason_When_ExceptionIsRollbackUnsupported()
    {
        var ex = new RollbackUnsupportedException(
            "loaded chunk in dimension 2", RollbackDegradeReason.MiniDimensionChunksLoaded);

        Assert.Equal(RollbackDegradeReason.MiniDimensionChunksLoaded, RollbackDegrade.Classify(ex));
        Assert.Equal("loaded chunk in dimension 2", ex.Message);
    }

    [Fact]
    public void Classify_Should_PreserveEveryReason_When_CarriedByTheTypedException()
    {
        foreach (RollbackDegradeReason reason in Enum.GetValues<RollbackDegradeReason>())
        {
            Assert.Equal(reason, RollbackDegrade.Classify(new RollbackUnsupportedException("boom", reason)));
        }
    }

    [Fact]
    public void Classify_Should_ReturnGenericBucket_When_ExceptionIsArbitrary()
    {
        Assert.Equal(
            RollbackDegradeReason.CaptureOrRestoreFailed,
            RollbackDegrade.Classify(new InvalidOperationException("simulated capture failure")));
    }

    [Fact]
    public void Classify_Should_Throw_When_ExceptionIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RollbackDegrade.Classify(null!));
    }

    [Fact]
    public void Describe_Should_ReturnAStablePhrase_When_GivenEachReason()
    {
        var expected = new Dictionary<RollbackDegradeReason, string>
        {
            [RollbackDegradeReason.CaptureOrRestoreFailed] = "capture or restore failed",
            [RollbackDegradeReason.PlayersJoined] = "players joined",
            [RollbackDegradeReason.MiniDimensionChunksLoaded] = "mini-dimension chunks loaded",
            [RollbackDegradeReason.EngineDrift] = "engine internals drifted",
            [RollbackDegradeReason.ModHookFailed] = "mod rollback hook failed",
        };

        foreach (RollbackDegradeReason reason in Enum.GetValues<RollbackDegradeReason>())
        {
            Assert.Equal(expected[reason], RollbackDegrade.Describe(reason));
        }
    }

    [Fact]
    public void RollbackAttempt_Should_CarryReasonAndDetail_When_Degraded()
    {
        RollbackAttempt attempt = RollbackAttempt.Degraded(
            RollbackDegradeReason.EngineDrift, "AtlasSetupException: layout changed");

        Assert.False(attempt.Succeeded);
        Assert.Equal(RollbackDegradeReason.EngineDrift, attempt.DegradeReason);
        Assert.Equal("AtlasSetupException: layout changed", attempt.DegradeDetail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RollbackAttempt_Should_CarryNoDegradeEvidenceButTheCaptureFlag_When_Succeeded(bool captured)
    {
        RollbackAttempt attempt = RollbackAttempt.Success(captured);

        Assert.True(attempt.Succeeded);
        Assert.Equal(captured, attempt.Captured);
        Assert.Null(attempt.DegradeDetail);
    }

    [Fact]
    public void RollbackAttempt_Should_RejectEmptyDetail_When_Degraded()
    {
        Assert.Throws<ArgumentException>(
            () => RollbackAttempt.Degraded(RollbackDegradeReason.EngineDrift, string.Empty));
    }
}
