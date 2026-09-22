// Client path C spike (docs/specs/2026-09-23-client-path-c.md). Throwaway measurement code,
// not shipped, not part of Atlas.slnx.
//
// The server boot (EmbeddedServerHost) and the client boot recipe (HeadlessClientBootstrap:
// hidden GLFW window + FBO, real ClientPlatformWindows, ClientMain built by constructor, the
// singleplayer DummyNetwork loopback in HeadlessClient.ConnectLoopback) are Zaldaryon.Pharos
// (MIT, Copyright (c) 2026 Zaldaryon), referenced here as a compiled library, not copied
// source. See LICENSE-PHAROS.md next to this file for the full notice. The one addition this
// spike makes on top of that recipe is the line marked VARIANT 1 below: calling the real
// ClientMain.Start() instead of Pharos's own minimal stand-in clientSystems array, which is
// the thing docs/specs/2026-07-17-client-side-testing.md and the Pharos report both found
// missing for a join to ever complete.
//
// Builds a hidden GLFW window: never run this directly on a real desktop session. Use
// run-nested.sh, which boots an isolated nested KWin/Xwayland session first and sets the
// environment this guard checks for.
using System.Diagnostics;
using System.Reflection;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

if (Environment.GetEnvironmentVariable("CLIENT_PATHC_NESTED") != "1"
    || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null
    || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
{
    Console.Error.WriteLine("refusing to run outside a nested display session: run via run-nested.sh, " +
        "which sets CLIENT_PATHC_NESTED=1 and DISPLAY and clears WAYLAND_DISPLAY once the nested " +
        "Xwayland is confirmed software-rendered.");
    return 1;
}

string variant = args.Length > 0 ? args[0] : "1";
if (variant is not ("1" or "baseline"))
{
    Console.Error.WriteLine($"unknown variant '{variant}': only \"1\" (calls Client.Start()) and " +
        "\"baseline\" (does not) exist.");
    return 1;
}

int joinTimeoutSeconds = args.Length > 1 ? int.Parse(args[1]) : 30;

static long RssMb() => Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

static string[] ThreadNames()
{
    // /proc/self/task/<tid>/comm gives the name set by Thread.Name (truncated to 15 chars by
    // the kernel); that is good enough to tell the engine's own threads apart from the CLR's.
    var names = new List<string>();
    foreach (var dir in Directory.GetDirectories("/proc/self/task"))
    {
        try
        {
            names.Add(File.ReadAllText(Path.Combine(dir, "comm")).Trim());
        }
        catch (IOException)
        {
            // Thread exited between the listing and the read; not a measurement error.
        }
    }

    return names.ToArray();
}

void Report(string label, Stopwatch total)
{
    Console.WriteLine($"[t+{total.ElapsedMilliseconds}ms] {label} RSS={RssMb()}MB threads={ThreadNames().Length}");
}

Console.WriteLine($"[probe] variant={variant} joinTimeout={joinTimeoutSeconds}s DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")} WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")}");

var total = Stopwatch.StartNew();

using var server = EmbeddedServerHost.Boot(new ServerWorldOptions { WorldName = "ClientPathC", Seed = "4242", PlayStyle = "creativebuilding", WorldType = "superflat" });
Report($"server booted, running={server.IsRunning}", total);

using var client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions { Width = 640, Height = 480, DisableAudio = true });
Report($"client booted, GL={client.RendererInfo?.Renderer}", total);

if (variant is "1")
{
    // VARIANT 1: run the client's own init path for real. ClientMain.Start() is where the
    // engine creates SystemNetworkProcess (the packet pump) and the other 7 client threads,
    // and builds the full 40-system clientSystems array; Pharos's own bootstrap never calls
    // this (it hand-assembles a 1-system stand-in instead, see HeadlessClientBootstrap.cs).
    try
    {
        client.Client.Start();
        Report("Client.Start() returned", total);
    }
    catch (Exception ex)
    {
        Report($"Client.Start() THREW: {ex.GetType().FullName}: {ex.Message}", total);
        Console.WriteLine(ex);
        Console.WriteLine($"[probe] clientSystems after throw: {DescribeSystems(client.Client)}");
        return 1;
    }
}

Console.WriteLine($"[probe] clientSystems={DescribeSystems(client.Client)}");

using var session = client.ConnectLoopback(server, "PathC");
Report("ConnectLoopback wired, Connect() returned", total);

bool joined = session.WaitForPlayerJoined(TimeSpan.FromSeconds(joinTimeoutSeconds));
Report($"WaitForPlayerJoined({joinTimeoutSeconds}s) -> {joined}, frames={session.FrameCount}, serverTicks={session.ServerTickCount}", total);

Console.WriteLine($"[probe] client.player={(client.Client.player == null ? "null" : client.Client.player.PlayerName)}");
Console.WriteLine($"[probe] server clients={server.Server.Clients.Count} [{string.Join("; ", server.Server.Clients.Values.Select(c => $"id={c.Id} name={c.PlayerName} state={c.State} sp={c.IsSinglePlayerClient}"))}]");
Console.WriteLine($"[probe] threads=[{string.Join(",", ThreadNames())}]");
Report("done", total);
return 0;

static string DescribeSystems(ClientMain client)
{
    var systems = client.clientSystems ?? Array.Empty<ClientSystem>();
    return $"{systems.Length} [{string.Join(",", systems.Select(s => s.Name))}]";
}
