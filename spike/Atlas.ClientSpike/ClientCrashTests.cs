using System.Text.RegularExpressions;

namespace Atlas.ClientSpike;

/// <summary>Step D: a client-side mod throws a few seconds after the player is in the world;
/// measure what the client does and what the scenario can learn. Same guard as the join test:
/// nothing runs unless SPIKE_RUN names a fresh run folder.</summary>
public class ClientCrashTests(ITestOutputHelper output)
{
    private static readonly Regex Uid = new(@"(player)?uid[ :=]+[A-Za-z0-9_=+/-]{10,}", RegexOptions.IgnoreCase);

    [Fact]
    public async Task ClientCrash_Should_BeMeasured()
    {
        string? run = Environment.GetEnvironmentVariable("SPIKE_RUN");
        string? mode = Environment.GetEnvironmentVariable("SPIKE_MODE");
        if (string.IsNullOrEmpty(run) || string.IsNullOrEmpty(mode))
        {
            output.WriteLine("SPIKE_RUN / SPIKE_MODE not set: no client started.");
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

        string modPath = Path.Combine(SpikePaths.SpikeRoot, "mods", mode);
        SandboxClient client = SandboxClient.Start(
            run, timeline, timeoutSeconds: 150, shotSeconds: 3,
            ["--connect", ep.Address, "--pw", ep.Password, "--addModPath", modPath]);
        string crashLog = Path.Combine(client.RunDir, "logs", "client-crash.log");
        try
        {
            var deadline = timeline.Now + TimeSpan.FromSeconds(140);
            TimeSpan? giveUpAt = null;
            while (timeline.Now < deadline && !client.Exited)
            {
                client.PollClientPid();
                if (timeline.Has("server.Playing (ConnectionState, polled each tick)") && giveUpAt == null)
                {
                    timeline.Mark("client.core-limit", client.CoreLimitLine());
                    timeline.Mark("client.rss-at-playing", $"{client.RssKb() / 1024} MB");
                    giveUpAt = timeline.Now + TimeSpan.FromSeconds(45);
                    timeline.Mark("scenario.playing-seen");
                }

                if (File.Exists(crashLog))
                {
                    timeline.Mark("run-folder.client-crash.log appeared");
                }

                if (client.ClientPid != null && !client.ClientAlive)
                {
                    timeline.Mark("client.process-gone");
                }

                if (client.ClientPid != null && client.HasDescendant("VSCrashReporter"))
                {
                    timeline.Mark("client.VSCrashReporter-process-seen");
                }

                if (giveUpAt is { } g && timeline.Now > g)
                {
                    timeline.Mark("scenario.gave-up-no-crash-within-45s");
                    break;
                }

                await Task.Delay(50);
            }

            if (client.Exited)
            {
                timeline.Mark("sandbox.exited");
            }
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
        output.WriteLine("---- files under logs/");
        foreach (string f in Directory.EnumerateFiles(Path.Combine(client.RunDir, "logs")))
        {
            output.WriteLine($"{new FileInfo(f).Length,8}  {Path.GetFileName(f)}");
        }

        output.WriteLine("---- files under tmp/ (the client's private TMPDIR)");
        foreach (string f in Directory.EnumerateFileSystemEntries(Path.Combine(client.RunDir, "tmp")))
        {
            output.WriteLine($"  {Path.GetFileName(f)}");
        }

        if (File.Exists(crashLog))
        {
            string text = Uid.Replace(File.ReadAllText(crashLog), "uid <redacted>");
            output.WriteLine("---- client-crash.log (redacted)");
            output.WriteLine(text);
        }
    }
}
