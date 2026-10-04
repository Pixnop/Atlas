using System.Net;
using System.Net.Sockets;
using Atlas.XUnit;
using Vintagestory.API.Config;

namespace Atlas.Engine.Tests;

/// <summary>Covers the <c>{{atlas:port:NAME}}</c> token end to end through the real xUnit
/// adapter: <c>[AtlasDataFiles]</c> seeds two files that name the tokens, the mod-config read a
/// mod would do in <c>StartServerSide</c> sees plain numbers, and
/// <see cref="IWorldSession.DataFilePort"/> returns the same numbers.</summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/port-tokens", TargetPath = "ModConfig")]
public class DataFilePortTests : AtlasScenarioBase
{
    [AtlasScenario]
    public Task DataFilePort_Should_ReturnThePortsTheModConfigHolds_When_FilesDeclareTokens()
    {
        PortsConfig? config = World.Api.LoadModConfig<PortsConfig>("ports-a.json");

        Assert.NotNull(config);
        Assert.Equal(World.DataFilePort("web"), config.Port);
        Assert.Equal(World.DataFilePort("admin"), config.Admin);
        Assert.NotEqual(config.Port, config.Admin);
        Assert.InRange(config.Port, 1024, 65535);

        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task DataFilePort_Should_GiveOnePortToANameUsedInTwoFiles_When_BothAreSeeded()
    {
        string other = File.ReadAllText(Path.Combine(GamePaths.ModConfig, "ports-b.cfg"));

        Assert.Equal($"web={World.DataFilePort("web")}\n", other);

        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task DataFilePort_Should_ReturnAPortAModCanBind_When_TheBootIsOver()
    {
        // What a mod that reads the port in StartServerSide does a moment later; the engine
        // holds nothing on it. Both protocols, because the draw promised both.
        int port = World.DataFilePort("web");

        var tcp = new TcpListener(IPAddress.Loopback, port);
        tcp.Start();
        tcp.Stop();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));

        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task DataFilePort_Should_ThrowNamingTheKnownNames_When_TheNameIsNotInAnyFile()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => World.DataFilePort("metrics"));

        Assert.Contains("'metrics'", ex.Message);
        Assert.Contains("admin, web", ex.Message);

        return Task.CompletedTask;
    }

    [AtlasScenario(RestartWorld = true)]
    public Task DataFilePort_Should_MatchTheNewHostsFiles_When_TheWorldRestarts()
    {
        // A restart boots a new host, which seeds again and draws again: whatever it drew, the
        // file the mod reads and the lookup agree.
        PortsConfig? config = World.Api.LoadModConfig<PortsConfig>("ports-a.json");

        Assert.NotNull(config);
        Assert.Equal(World.DataFilePort("web"), config.Port);
        Assert.Equal(World.DataFilePort("admin"), config.Admin);

        return Task.CompletedTask;
    }

    private sealed class PortsConfig
    {
        public int Port { get; set; }

        public int Admin { get; set; }
    }
}
