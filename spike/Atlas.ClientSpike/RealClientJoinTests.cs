namespace Atlas.ClientSpike;

/// <summary>Steps B and C: a real client, through the sandbox, joins the embedded host. Only runs
/// when SPIKE_RUN names a fresh run folder, so a plain "dotnet test" never starts a client.</summary>
public class RealClientJoinTests(ITestOutputHelper output)
{
    private static string? RunName => Environment.GetEnvironmentVariable("SPIKE_RUN");

    private static int EnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;

    [Fact]
    public async Task RealClient_Should_JoinAndReachPlaying()
    {
        if (RunName is not { Length: > 0 } run)
        {
            output.WriteLine("SPIKE_RUN not set: no client started.");
            return;
        }

        var timeline = new Timeline();
        await using ServerHost host = new(new WorldOptions(), [], SpikePaths.OwnOutputDirectory)
        {
            OpenClientListener = true,
            BypassCharacterGate = Environment.GetEnvironmentVariable("SPIKE_BYPASS") == "1",
        };
        await host.StartAsync();
        timeline.Mark("host.ready", $"bypass={host.BypassCharacterGate}");
        ClientEndpoint ep = host.ClientEndpoint!;
        ServerObserver observer = await ServerObserver.AttachAsync(host, timeline);

        int timeout = EnvInt("SPIKE_TIMEOUT", 120);
        int hold = EnvInt("SPIKE_HOLD", 8);
        SandboxClient client = SandboxClient.Start(
            run, timeline, timeout, shotSeconds: 5, ["--connect", ep.Address, "--pw", ep.Password]);
        try
        {
            // Wait on the SERVER side for Playing (or give up when the client is gone or late).
            var deadline = timeline.Now + TimeSpan.FromSeconds(timeout - 5);
            while (timeline.Now < deadline && !timeline.Has("server.Playing (ConnectionState, polled each tick)") && !client.Exited)
            {
                client.PollClientPid();
                await Task.Delay(100);
            }

            client.PollClientPid();
            timeline.Mark("client.core-limit", client.CoreLimitLine());
            if (timeline.Has("server.Playing (ConnectionState, polled each tick)"))
            {
                await Task.Delay(TimeSpan.FromSeconds(hold));
                timeline.Mark("client.rss-at-hold-end", $"{client.RssKb() / 1024} MB");
            }
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(40));
            timeline.Mark("client.stopped");
        }

        string dump = timeline.Dump();
        output.WriteLine(dump);
        File.WriteAllText(Path.Combine(client.RunDir, "server-view.txt"), dump + Environment.NewLine);
        Assert.True(timeline.Has("server.identification"), "the server never saw an identification");
        Assert.True(timeline.Has("server.PlayerJoin"), "the client never reached the join request");
        Assert.True(timeline.Has("server.PlayerNowPlaying (packet 26, level finalize handled)"), "level finalize never completed");
        Assert.True(timeline.Has("server.Playing (ConnectionState, polled each tick)"), "the player never reached Playing");
        Assert.NotNull(observer.PlayerName);
    }
}
