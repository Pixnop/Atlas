using Atlas.Internal.Rollback;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.ClientSpike;

/// <summary>Step E: world rollback while a real client is connected. SPIKE_E=1: the snapshot is
/// captured before the client joins (the client is post-capture). SPIKE_E=2: the snapshot is
/// captured with the client in (the client is a captured player), then the player is moved and
/// the world rolled back.</summary>
public class ClientRollbackTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Rollback_WithRealClient_Should_BeMeasured()
    {
        string? run = Environment.GetEnvironmentVariable("SPIKE_RUN");
        string? scenario = Environment.GetEnvironmentVariable("SPIKE_E");
        if (string.IsNullOrEmpty(run) || string.IsNullOrEmpty(scenario))
        {
            output.WriteLine("SPIKE_RUN / SPIKE_E not set: no client started.");
            return;
        }

        var timeline = new Timeline();
        await using ServerHost host = new(new WorldOptions(), [], SpikePaths.OwnOutputDirectory)
        {
            OpenClientListener = true,
            BypassCharacterGate = true,
        };
        await host.StartAsync();
        timeline.Mark("host.ready");
        ClientEndpoint ep = host.ClientEndpoint!;
        await ServerObserver.AttachAsync(host, timeline);

        if (scenario == "1")
        {
            RollbackAttempt first = await host.TryRollbackWorldAsync();
            timeline.Mark("rollback#1 (capture, no client yet)", $"succeeded={first.Succeeded} captured={first.Captured}");
        }

        SandboxClient client = SandboxClient.Start(
            run, timeline, timeoutSeconds: 120, shotSeconds: 3, ["--connect", ep.Address, "--pw", ep.Password]);
        try
        {
            var deadline = timeline.Now + TimeSpan.FromSeconds(100);
            while (timeline.Now < deadline && !timeline.Has("server.Playing (ConnectionState, polled each tick)") && !client.Exited)
            {
                client.PollClientPid();
                await Task.Delay(100);
            }

            Assert.True(timeline.Has("server.Playing (ConnectionState, polled each tick)"), "client never reached Playing");
            await Task.Delay(TimeSpan.FromSeconds(6));

            if (scenario == "2")
            {
                RollbackAttempt capture = await host.TryRollbackWorldAsync();
                timeline.Mark("rollback#1 (capture, client in)", $"succeeded={capture.Succeeded} captured={capture.Captured}");
                await host.RunOnGameThreadAsync((api, ticks) =>
                {
                    IServerPlayer p = api.World.AllOnlinePlayers.OfType<IServerPlayer>().First();
                    var pos = p.Entity.Pos;
                    timeline.Mark("server.position-at-capture", $"{pos.X:F1} {pos.Y:F1} {pos.Z:F1}");
                    p.Entity.TeleportToDouble(pos.X + 40, pos.Y + 25, pos.Z + 40);
                    return Task.CompletedTask;
                });
                await Task.Delay(TimeSpan.FromSeconds(6));
                await host.RunOnGameThreadAsync((api, ticks) =>
                {
                    IServerPlayer p = api.World.AllOnlinePlayers.OfType<IServerPlayer>().First();
                    var pos = p.Entity.Pos;
                    timeline.Mark("server.position-after-teleport", $"{pos.X:F1} {pos.Y:F1} {pos.Z:F1}");
                    return Task.CompletedTask;
                });
            }

            timeline.Mark("rollback.calling");
            RollbackAttempt restore = await host.TryRollbackWorldAsync();
            timeline.Mark("rollback.returned", $"succeeded={restore.Succeeded} captured={restore.Captured} degrade={restore.Degrade}");

            for (int i = 0; i < 16; i++)
            {
                int k = i;
                await host.RunOnGameThreadAsync((api, ticks) =>
                {
                    IServerPlayer? p = api.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault();
                    timeline.Mark($"server.position+{k * 0.5:F1}s-after-rollback-returned", p == null ? "no player" : $"{p.Entity.Pos.X:F1} {p.Entity.Pos.Y:F1} {p.Entity.Pos.Z:F1}");
                    return Task.CompletedTask;
                });
                await Task.Delay(500);
            }

            timeline.Mark("client.alive-8s-after-rollback", $"{client.ClientAlive}");
            await host.RunOnGameThreadAsync((api, ticks) =>
            {
                var online = api.World.AllOnlinePlayers.OfType<IServerPlayer>().ToList();
                timeline.Mark("server.online-after-rollback", $"count={online.Count} " + string.Join(", ", online.Select(p => $"{p.PlayerName}:{p.ConnectionState}@{p.Entity?.Pos.X:F1},{p.Entity?.Pos.Y:F1},{p.Entity?.Pos.Z:F1}")));
                return Task.CompletedTask;
            });
            await Task.Delay(TimeSpan.FromSeconds(6));
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(40));
            timeline.Mark("client.stop-returned");
        }

        string dump = timeline.Dump();
        output.WriteLine(dump);
        File.WriteAllText(Path.Combine(client.RunDir, "server-view.txt"), dump + Environment.NewLine);
        output.WriteLine("---- sandbox.log");
        output.WriteLine(File.ReadAllText(Path.Combine(client.RunDir, "sandbox.log")));
    }
}
