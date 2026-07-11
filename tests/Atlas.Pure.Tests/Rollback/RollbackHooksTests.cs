using Atlas.Internal.Rollback;
using Vintagestory.API.Datastructures;

namespace Atlas.Pure.Tests.Rollback;

/// <summary>Pins the mod cooperation contract of world rollback (stage 3): the event names and
/// the versioned payload shapes are public API in every sense but the C# one (mods subscribe by
/// the literal event name and read the payload by key, referencing only VintagestoryAPI), so a
/// rename or a shape change here is a breaking change for every cooperating mod. These tests
/// make that impossible to do by accident.</summary>
public class RollbackHooksTests
{
    [Fact]
    public void EventNames_Should_BeTheDocumentedContract()
    {
        Assert.Equal("atlas:rollback:captured", RollbackHooks.CapturedEventName);
        Assert.Equal("atlas:rollback:restored", RollbackHooks.RestoredEventName);
    }

    [Fact]
    public void CapturedPayload_Should_CarryVersionAndGeneration()
    {
        TreeAttribute payload = RollbackHooks.CapturedPayload(generation: 7);

        Assert.Equal(1, payload.GetInt(RollbackHooks.VersionKey));
        Assert.Equal(RollbackHooks.PayloadVersion, payload.GetInt(RollbackHooks.VersionKey));
        Assert.Equal(7, payload.GetInt(RollbackHooks.GenerationKey));
        Assert.False(
            payload.HasAttribute(RollbackHooks.RestoreCountKey),
            "the captured payload must not carry a restore count: nothing was restored");
    }

    [Fact]
    public void RestoredPayload_Should_CarryVersionGenerationAndRestoreCount()
    {
        TreeAttribute payload = RollbackHooks.RestoredPayload(generation: 7, restoreCount: 3);

        Assert.Equal(RollbackHooks.PayloadVersion, payload.GetInt(RollbackHooks.VersionKey));
        Assert.Equal(7, payload.GetInt(RollbackHooks.GenerationKey));
        Assert.Equal(3, payload.GetInt(RollbackHooks.RestoreCountKey));
    }

    [Fact]
    public void PayloadKeys_Should_BeTheDocumentedContract()
    {
        Assert.Equal("version", RollbackHooks.VersionKey);
        Assert.Equal("generation", RollbackHooks.GenerationKey);
        Assert.Equal("restoreCount", RollbackHooks.RestoreCountKey);
    }

    [Fact]
    public void RollbackUnsupportedException_Should_PreserveTheHandlerException_When_WrappingIt()
    {
        var handlerFailure = new InvalidOperationException("handler boom");

        var wrapped = new RollbackUnsupportedException(
            "World rollback: a mod's 'atlas:rollback:restored' hook handler threw " +
            "InvalidOperationException: handler boom",
            RollbackDegradeReason.ModHookFailed,
            handlerFailure);

        Assert.Equal(RollbackDegradeReason.ModHookFailed, wrapped.Reason);
        Assert.Same(handlerFailure, wrapped.InnerException);
        Assert.Equal(RollbackDegradeReason.ModHookFailed, RollbackDegrade.Classify(wrapped));
        Assert.Contains("InvalidOperationException: handler boom", wrapped.Message);
    }
}
