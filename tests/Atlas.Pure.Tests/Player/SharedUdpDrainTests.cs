using System.Reflection;
using Atlas.Internal.Player;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server.Network;

namespace Atlas.Pure.Tests.Player;

/// <summary>Tests for the host's one listener that empties the shared dummy UDP connection's
/// client receive buffer, over the engine's real <see cref="DummyUdpNetServer"/> and
/// <see cref="DummyNetwork"/>: what gets cleared, when it does nothing, and that it takes the
/// engine's own lock. The live host is in the E2E suite.</summary>
public class SharedUdpDrainTests
{
    [Fact]
    public void Install_Should_RegisterExactlyOneListener_EveryPass_WithAnErrorHandler()
    {
        var api = Substitute.For<ICoreServerAPI>();

        SharedUdpDrain.Install(api, () => null);

        // The overload with the handler: without one, a throw would abort the rest of the pass.
        var registrations = api.Event.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEventAPI.RegisterGameTickListener))
            .Select(call => call.GetArguments())
            .ToArray();
        object?[] registration = Assert.Single(registrations);
        Assert.NotNull(registration[0]);
        Assert.NotNull(registration[1]);
        Assert.Equal(1, registration[2]);
    }

    [Fact]
    public void Listener_Should_EmptyTheSharedQueue_And_LeaveTheServerReceiveSideAlone()
    {
        var udp = new Udp();
        udp.SendToPlayers(5);
        udp.ServerQueue().Enqueue(new Packet_UdpPacket());
        Action<float> listener = Register(() => udp.Server);

        listener(0.033f);

        Assert.Empty(udp.ClientQueue());
        Assert.Single(udp.ServerQueue());

        // Packets sent after a pass are there for the next one.
        udp.SendToPlayers(2);
        Assert.Equal(2, udp.ClientQueue().Count);
        listener(0.033f);
        Assert.Empty(udp.ClientQueue());
    }

    [Fact]
    public void Listener_Should_DoNothing_UntilATestPlayerCreatedTheUdpServer()
    {
        // Slot 0 is empty before the first join; a pass must not throw.
        UNetServer? slot = null;
        Action<float> listener = Register(() => slot);

        listener(0.033f);

        // The engine's real UDP server (a dedicated host's), or anything else: not Atlas's to touch.
        slot = Substitute.For<UNetServer>();
        listener(0.033f);
        Assert.Empty(slot.ReceivedCalls());

        // Once the dummy server is there, the next pass clears it.
        var udp = new Udp();
        udp.SendToPlayers(3);
        slot = udp.Server;
        listener(0.033f);
        Assert.Empty(udp.ClientQueue());
    }

    [Fact]
    public void Listener_Should_NotAllocate_OnAPass()
    {
        // It runs inside every server pass, so MeasureTicks sees it: reading three fields and
        // taking a lock must cost nothing on the heap.
        var udp = new Udp();
        Action<float> listener = Register(() => udp.Server);
        var packets = Enumerable.Range(0, 3).Select(_ => new Packet_UdpPacket()).ToArray();
        for (int warm = 0; warm < 20; warm++)
        {
            udp.Enqueue(packets);
            listener(0.033f);
        }

        long allocated = 0;
        for (int pass = 0; pass < 500; pass++)
        {
            udp.Enqueue(packets);
            long before = GC.GetAllocatedBytesForCurrentThread();
            listener(0.033f);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Clear_Should_ReturnTheNumberDropped_And_ReadTheQueueTheServerReallyUses()
    {
        var udp = new Udp();
        udp.SendToPlayers(10);

        Assert.Equal(10, EngineCompat.ClearUdpClientBuffer(udp.Server));
        Assert.Empty(udp.ClientQueue());
        Assert.Equal(0, EngineCompat.ClearUdpClientBuffer(udp.Server));
    }

    [Fact]
    public void Clear_Should_WaitForTheEnginesLock_BeforeTouchingTheQueue()
    {
        var udp = new Udp();
        udp.SendToPlayers(3);
        int dropped = -1;
        Thread clearer;

        lock (udp.ClientLock())
        {
            clearer = new Thread(() => dropped = EngineCompat.ClearUdpClientBuffer(udp.Server));
            clearer.Start();

            // Blocked on the monitor we hold: it cannot have emptied anything.
            Assert.True(SpinWait.SpinUntil(() => clearer.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TimeSpan.FromSeconds(10)));
            Assert.Equal(3, udp.ClientQueue().Count);
        }

        Assert.True(clearer.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(3, dropped);
        Assert.Empty(udp.ClientQueue());
    }

    private static Action<float> Register(Func<UNetServer?> slot)
    {
        Action<float>? listener = null;
        var api = Substitute.For<ICoreServerAPI>();
        api.Event
            .RegisterGameTickListener(Arg.Any<Action<float>>(), Arg.Any<Action<Exception>>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(call =>
            {
                listener = call.ArgAt<Action<float>>(0);
                return 1L;
            });

        SharedUdpDrain.Install(api, slot);

        return listener!;
    }

    /// <summary>The engine's own dummy UDP server over its own network, wired the way
    /// <c>DummyClientConnector.Connect</c> wires it.</summary>
    private sealed class Udp
    {
        private readonly DummyNetwork _network = new();

        public Udp()
        {
            _network.Start();
            Server = new DummyUdpNetServer();
            Server.SetNetwork(_network);
        }

        public DummyUdpNetServer Server { get; }

        public void SendToPlayers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Server.SendToClient(1, new Packet_UdpPacket());
            }
        }

        public void Enqueue(Packet_UdpPacket[] packets)
        {
            foreach (Packet_UdpPacket packet in packets)
            {
                Server.SendToClient(1, packet);
            }
        }

        public Queue<object> ClientQueue() => Field<Queue<object>>("ClientReceiveBuffer");

        public Queue<object> ServerQueue() => Field<Queue<object>>("ServerReceiveBuffer");

        public object ClientLock() => Field<object>("ClientReceiveBufferLock");

        private T Field<T>(string name)
            => (T)typeof(DummyNetwork).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_network)!;
    }
}
