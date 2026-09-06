using Atlas.Bridge;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Pure.Tests.Bridge;

/// <summary>Pins the handoff between the two copies of AtlasBridge.dll: the engine-side
/// <see cref="BridgeRendezvous"/> installs two AppDomain data slots, the mod-side
/// <see cref="BridgeModSystem"/> the game's ModLoader creates reads them. Nothing here boots a
/// server; the only other coverage of this contract runs two of them (SupersededHostTests).
/// </summary>
/// <remarks>Both sides live in one class on purpose: the tests write process-wide statics and
/// AppDomain slots, and xUnit serializes a class's tests while parallelizing across classes.
/// Every test starts from <c>Reset</c>, which is also the state a host boot leaves behind.</remarks>
public class BridgeRendezvousTests
{
    [Fact]
    public void Reset_Should_InstallBothSlots_When_Called()
    {
        BridgeRendezvous.Reset();

        // The literal names are the wire format between the two assembly copies: the mod-side
        // copy has them inlined from ITS build, so a rename is a silent handoff failure.
        Assert.Equal("atlas.bridge.publishApi", BridgeRendezvous.PublishApiSlot);
        Assert.Equal("atlas.bridge.onTick", BridgeRendezvous.TickSlot);
        Assert.IsType<Action<object>>(AppDomain.CurrentDomain.GetData("atlas.bridge.publishApi"), exactMatch: false);
        Assert.IsType<Action>(AppDomain.CurrentDomain.GetData("atlas.bridge.onTick"), exactMatch: false);
    }

    [Fact]
    public void Reset_Should_SwapApiReadyForAFreshOne_When_ThePreviousOneCompleted()
    {
        BridgeRendezvous.Reset();
        BridgeRendezvous.PublishApi(Substitute.For<ICoreServerAPI>());
        Task<ICoreServerAPI> completed = BridgeRendezvous.ApiReady;
        Assert.True(completed.IsCompletedSuccessfully);

        BridgeRendezvous.Reset();

        // The identity swap ServerHost.IsSuperseded depends on: a host that booted before this
        // Reset keeps the OLD task, so it can tell it is no longer the live one.
        Assert.NotSame(completed, BridgeRendezvous.ApiReady);
        Assert.False(BridgeRendezvous.ApiReady.IsCompleted);
    }

    [Fact]
    public void Reset_Should_DropTickSubscribers_When_ThePreviousHostIsGone()
    {
        BridgeRendezvous.Reset();
        int stale = 0;
        BridgeRendezvous.TickFired += () => stale++;

        BridgeRendezvous.Reset();
        BridgeRendezvous.NotifyTick();

        Assert.Equal(0, stale);
    }

    [Fact]
    public void TickSlot_Should_RaiseTickFired_When_TheModInvokesIt()
    {
        BridgeRendezvous.Reset();
        int ticks = 0;
        BridgeRendezvous.TickFired += () => ticks++;

        var onTick = (Action)AppDomain.CurrentDomain.GetData(BridgeRendezvous.TickSlot)!;
        onTick();
        onTick();

        Assert.Equal(2, ticks);
    }

    [Fact]
    public async Task PublishApiSlot_Should_CompleteApiReadyWithTheInstance_When_TheModInvokesIt()
    {
        BridgeRendezvous.Reset();
        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();

        // The slot is typed Action<object>, never Action<ICoreServerAPI>: only framework types
        // cross the two assembly copies, and the API type itself is shared through the install.
        var publish = (Action<object>)AppDomain.CurrentDomain.GetData(BridgeRendezvous.PublishApiSlot)!;
        publish(api);

        Assert.True(BridgeRendezvous.ApiReady.IsCompletedSuccessfully);
        Assert.Same(api, await BridgeRendezvous.ApiReady);
    }

    [Fact]
    public async Task StartServerSide_Should_ListenForTicksAndPublishTheApi_When_TheModLoaderStartsTheBridge()
    {
        BridgeRendezvous.Reset();
        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();
        int ticks = 0;
        BridgeRendezvous.TickFired += () => ticks++;

        new BridgeModSystem().StartServerSide(api);

        Assert.Same(api, await BridgeRendezvous.ApiReady);
        var listener = (Action<float>)api.Event.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IServerEventAPI.RegisterGameTickListener))
            .GetArguments()[0]!;
        listener(0f);
        Assert.Equal(1, ticks);
    }

    [Fact]
    public void ShouldLoad_Should_BeServerSideOnly_When_TheModLoaderAsks()
    {
        var bridge = new BridgeModSystem();

        Assert.True(bridge.ShouldLoad(EnumAppSide.Server));
        Assert.False(bridge.ShouldLoad(EnumAppSide.Client));
    }
}
