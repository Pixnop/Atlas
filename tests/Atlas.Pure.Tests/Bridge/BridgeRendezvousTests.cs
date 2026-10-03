using Atlas.Bridge;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Pure.Tests.Bridge;

/// <summary>Pins the handoff between the two copies of AtlasBridge.dll: the engine-side
/// <see cref="BridgeRendezvous"/> installs three AppDomain data slots, the mod-side
/// <see cref="BridgeModSystem"/> and <see cref="BridgeModsPreSystem"/> the game's ModLoader
/// creates read them. Nothing here boots a server; the only other coverage of this contract runs
/// two of them (SupersededHostTests).</summary>
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

    [Fact]
    public void Reset_Should_DropModsPreSubscribers_When_ThePreviousHostIsGone()
    {
        BridgeRendezvous.Reset();
        int stale = 0;
        BridgeRendezvous.ModsPre += _ => stale++;

        BridgeRendezvous.Reset();
        BridgeRendezvous.NotifyModsPre([]);

        Assert.Equal(0, stale);
    }

    [Fact]
    public void ModsPreSlot_Should_RaiseModsPre_When_TheModInvokesIt()
    {
        BridgeRendezvous.Reset();
        IEnumerable<Mod> mods = [];
        IEnumerable<Mod>? seen = null;
        BridgeRendezvous.ModsPre += m => seen = m;

        var publish = (Action<object>)AppDomain.CurrentDomain.GetData(BridgeRendezvous.ModsPreSlot)!;
        publish(mods);

        Assert.Same(mods, seen);
    }

    [Fact]
    public void StartPre_Should_PublishTheLoadedMods_When_TheModLoaderStartsTheBridge()
    {
        BridgeRendezvous.Reset();
        ICoreAPI api = Substitute.For<ICoreAPI>();
        IEnumerable<Mod> mods = [];
        api.ModLoader.Mods.Returns(mods);
        IEnumerable<Mod>? seen = null;
        BridgeRendezvous.ModsPre += m => seen = m;

        new BridgeModsPreSystem().StartPre(api);

        Assert.Same(mods, seen);
    }

    [Fact]
    public void Reset_Should_EmptyTheEarlyJoinSlot_When_ThePreviousHostRegisteredOne()
    {
        BridgeRendezvous.Reset();
        BridgeRendezvous.RegisterEarlyJoin(_ => { });
        Assert.NotNull(AppDomain.CurrentDomain.GetData(BridgeRendezvous.EarlyJoinSlot));

        BridgeRendezvous.Reset();

        // The slot is process-wide: a host that did not ask for an early handler must not
        // inherit the one the previous host left behind.
        Assert.Null(AppDomain.CurrentDomain.GetData(BridgeRendezvous.EarlyJoinSlot));
    }

    [Fact]
    public void EarlyJoinSlot_Should_BeFilledOnlyOnRequest_When_TheHostBoots()
    {
        // The literal name is the wire format between the two assembly copies, like the others.
        Assert.Equal("atlas.bridge.earlyJoin", BridgeRendezvous.EarlyJoinSlot);

        BridgeRendezvous.Reset();

        Assert.Null(AppDomain.CurrentDomain.GetData("atlas.bridge.earlyJoin"));
    }

    [Fact]
    public void EarlyJoinSlot_Should_CarryTheHandlerAsAnObjectAction_When_Registered()
    {
        BridgeRendezvous.Reset();
        IServerPlayer? seen = null;
        BridgeRendezvous.RegisterEarlyJoin(player => seen = player);
        IServerPlayer joining = Substitute.For<IServerPlayer>();

        // Typed Action<object>, like the API slot: only framework types cross the two copies.
        var slot = (Action<object>)AppDomain.CurrentDomain.GetData(BridgeRendezvous.EarlyJoinSlot)!;
        slot(joining);

        Assert.Same(joining, seen);
    }

    [Fact]
    public void RegisterEarlyJoin_Should_RejectANullHandler_When_Called()
    {
        BridgeRendezvous.Reset();

        Assert.Throws<ArgumentNullException>(() => BridgeRendezvous.RegisterEarlyJoin(null!));
    }

    [Fact]
    public void StartPre_Should_RegisterAPlayerJoinHandlerThatCallsTheHost_When_TheHostAsked()
    {
        BridgeRendezvous.Reset();
        IServerPlayer? seen = null;
        BridgeRendezvous.RegisterEarlyJoin(player => seen = player);
        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();

        new BridgeModsPreSystem().StartPre(api);

        var handler = (PlayerDelegate)api.Event.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == "add_PlayerJoin")
            .GetArguments()[0]!;
        IServerPlayer joining = Substitute.For<IServerPlayer>();
        handler(joining);
        Assert.Same(joining, seen);
    }

    [Fact]
    public void StartPre_Should_RegisterNoPlayerJoinHandler_When_TheHostDidNotAsk()
    {
        BridgeRendezvous.Reset();
        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();

        new BridgeModsPreSystem().StartPre(api);

        Assert.DoesNotContain(api.Event.ReceivedCalls(), call => call.GetMethodInfo().Name == "add_PlayerJoin");
    }

    [Fact]
    public void StartPre_Should_RegisterNothing_When_TheApiIsNotAServerApi()
    {
        BridgeRendezvous.Reset();
        BridgeRendezvous.RegisterEarlyJoin(_ => throw new InvalidOperationException("must not be called"));
        ICoreAPI api = Substitute.For<ICoreAPI>();

        // A client-side API has no PlayerJoin; the bridge only ever loads on the server, and the
        // pre-system must not trip over an API that cannot take the handler.
        new BridgeModsPreSystem().StartPre(api);

        Assert.DoesNotContain(api.Event.ReceivedCalls(), call => call.GetMethodInfo().Name == "add_PlayerJoin");
    }

    [Fact]
    public void ModsPreSystem_ShouldLoad_Should_BeServerSideOnly_When_TheModLoaderAsks()
    {
        var bridge = new BridgeModsPreSystem();

        Assert.True(bridge.ShouldLoad(EnumAppSide.Server));
        Assert.False(bridge.ShouldLoad(EnumAppSide.Client));
    }

    [Fact]
    public void ModsPreSystem_ExecuteOrder_Should_BeTheLowestValue_When_Read()
        => Assert.Equal(-1_000_000d, new BridgeModsPreSystem().ExecuteOrder());
}
