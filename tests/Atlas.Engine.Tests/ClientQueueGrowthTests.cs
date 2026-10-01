using Atlas.Internal.Player;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers issue #185 end to end: a joined test player that never reads
/// <see cref="ITestPlayer.Client"/> while entities move near it must not make the embedded
/// server's outbound queues pile up. The UDP queue every test player shares is emptied on every
/// pass, and of the TCP messages only the kinds <see cref="IClientObservations"/> decodes are
/// kept, so what the player holds is what a scenario can read.</summary>
/// <remarks>Measured on 1.22.3 and 1.21.7 with one player and 100 hens, over 3000 passes before the
/// fix: the UDP queue reached about 3000 packets (13 to 18 MB retained) and the player's own
/// receive side 2600 to 3500 parked messages (3.5 MB), about a quarter of them kinds Atlas never
/// decodes (the rest are mod-channel packets, which it keeps).
/// The run here is a third of that length, which is plenty: the queues grew by about one packet
/// per pass.</remarks>
[Trait("Category", "E2E")]
public class ClientQueueGrowthTests
{
    private const string Hen = "game:chicken-hen";
    private const int Hens = 100;
    private const int Passes = 1000;
    private const int WarmUp = 100;

    /// <summary>What a pass can legitimately leave in the UDP queue: whatever the engine sent after
    /// the drain ran, within the same pass. The unfixed queue reached about 1000 here.</summary>
    private const int QueueBound = 50;

    /// <summary>The parked messages that are not mod-channel packets: the join's own entity, player
    /// data and chat, and one entity packet per hen (a hen can arrive again when it comes back
    /// into range). The unfixed list held about 480 by now.</summary>
    private const int OtherKindsBound = 250;

    [Fact]
    public async Task UnreadPlayer_Should_HoldNoUdpPackets_And_NoPacketKindItDoesNotDecode_When_EntitiesMoveNearIt()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("QueueWatcher");
            await world.Ticks(30);
            int spawnedAt = world.CurrentTick;
            Entity[] hens =
            [
                .. Enumerable.Range(0, Hens).Select(i => world.SpawnEntity(Hen, world.Spawn.Offset((i % 10) - 5, 1, (i / 10) - 5))),
            ];

            // The hens' own arrival (one packet each, up to 31 passes after the spawn) is a burst
            // that the next pass's listener takes out; the run is measured once it is over.
            await world.Ticks(WarmUp);

            // The hens wander and animate for the whole run; nothing reads the player. Sampled
            // after every pass, so the worst moment counts, not a lucky one.
            int udpPeak = 0;
            for (int pass = 0; pass < Passes; pass++)
            {
                await world.Ticks(1);
                udpPeak = Math.Max(udpPeak, EngineProbes.UdpClientBufferCount(world.Api));
            }

            Assert.True(udpPeak <= QueueBound, $"the shared UDP queue reached {udpPeak} packets over {Passes} passes");

            // What is parked is what a read can decode, by the engine's own deserializer: every
            // sub-message the drain dispatches on, or the identification packet (the one id the
            // serializer omits, which cannot be told apart and is kept). Nothing else.
            Packet_Server[] parked = EngineProbes.ParkedPackets(player);
            int[] undecoded = [.. parked.Where(packet => !IsDecodedKind(packet) && packet.Id != 1).Select(packet => packet.Id).Distinct().Order()];
            Assert.True(undecoded.Length == 0, $"packets of kinds that are never decoded were kept, ids {string.Join(", ", undecoded)}");
            int others = parked.Count(packet => packet.CustomPacket == null);
            Assert.True(others <= OtherKindsBound, $"{others} parked messages that are not mod-channel packets");

            // The kept kinds are all still there, in order, with their ticks: a first read after
            // all those passes finds every hen, and the player's own join.
            IReadOnlyList<ReceivedEntity> arrivals = player.Client.EntityArrivals();
            Assert.All(hens, hen => Assert.True(player.Client.HasReceivedEntity(hen.EntityId), $"hen {hen.EntityId} never arrived"));
            Assert.Contains(arrivals, a => a.EntityId == player.Entity.EntityId && a.Path == EntityArrivalPath.JoinList);
            Assert.All(
                arrivals.Where(a => hens.Any(hen => hen.EntityId == a.EntityId)),
                a => Assert.InRange(a.Tick, spawnedAt, world.CurrentTick));
            Assert.Equal(arrivals.OrderBy(a => a.Sequence).ToArray(), arrivals.ToArray());
            Assert.Equal(arrivals.Select(a => a.Tick).Order().ToArray(), arrivals.Select(a => a.Tick).ToArray());
            Assert.Empty(EngineProbes.ParkedPackets(player));

            // And it keeps working afterwards: an entity and a chat line sent now are observed,
            // stamped with a pass at or after the send.
            int sentAt = world.CurrentTick;
            Entity late = world.SpawnEntity(Hen, world.Spawn.Offset(0, 1, 6));
            player.Player.SendMessage(GlobalConstants.GeneralChatGroup, "after the long run", EnumChatType.Notification);
            Assert.Contains("after the long run", player.Client.ChatLines());
            await world.Until(() => player.Client.HasReceivedEntity(late.EntityId), 400);
            Assert.All(
                player.Client.EntityArrivals().Where(a => a.EntityId == late.EntityId),
                a => Assert.InRange(a.Tick, sentAt, world.CurrentTick));
        });
    }

    [Fact]
    public async Task SharedUdpDrain_Should_BeRegisteredOncePerHost_Whatever_PlayersJoinAndLeave()
    {
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            Assert.Equal(1, EngineProbes.UdpDrainListeners(world.Api));

            ITestPlayer first = await world.JoinPlayer("DrainOne");
            await world.JoinPlayer("DrainTwo");
            await world.JoinPlayer("DrainThree");
            Assert.Equal(1, EngineProbes.UdpDrainListeners(world.Api));

            first.Player.Disconnect("leaving");
            await world.Until(() => !first.IsConnected, 400);
            await world.Ticks(5);
            Assert.Equal(1, EngineProbes.UdpDrainListeners(world.Api));
        });
    }

    /// <summary>Whether the packet carries one of the sub-messages the drain decodes, the way it
    /// dispatches on them.</summary>
    private static bool IsDecodedKind(Packet_Server packet)
        => packet.HighlightBlocks != null
            || packet.SpawnParticles != null
            || packet.CustomPacket != null
            || packet.Chatline != null
            || packet.Entity != null
            || packet.EntitySpawn != null
            || packet.Entities != null
            || packet.PlayerData != null
            || packet.PlayerGroups != null
            || packet.PlayerGroup != null;
}
