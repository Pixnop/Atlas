using System.Diagnostics;
using System.Globalization;

namespace Atlas.ClientSpike;

/// <summary>Runs the stock client through sandbox.sh and nothing else. Never touches the
/// client's data path, never sets a display.</summary>
internal sealed class SandboxClient : IAsyncDisposable
{
    private readonly Process _sandbox;
    private readonly Timeline _timeline;

    private SandboxClient(Process sandbox, Timeline timeline, string runDir)
    {
        _sandbox = sandbox;
        _timeline = timeline;
        RunDir = runDir;
    }

    public string RunDir { get; }

    public int SandboxPid => _sandbox.Id;

    public bool Exited => _sandbox.HasExited;

    public int? ClientPid { get; private set; }

    public static SandboxClient Start(
        string runName, Timeline timeline, int timeoutSeconds, int shotSeconds, IEnumerable<string> clientArgs)
    {
        string runDir = SpikePaths.RunDir(runName);
        if (Directory.Exists(runDir))
        {
            throw new InvalidOperationException($"Run name '{runName}' already exists; run names must be new.");
        }

        // ulimit -c 0 first (inherited by everything the sandbox starts): the kernel pipes core
        // dumps to systemd-coredump on this machine, and a core of a logged-in client holds its
        // session. The sandbox script itself is untouched.
        var psi = new ProcessStartInfo("/usr/bin/bash") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("ulimit -c 0; exec \"$0\" \"$@\"");
        psi.ArgumentList.Add(SpikePaths.Sandbox);
        psi.ArgumentList.Add(runName);
        foreach (string a in clientArgs)
        {
            psi.ArgumentList.Add(a);
        }

        // The sandbox builds the client's own environment from a whitelist; the child of this
        // process still gets none of the desktop's display or bus variables.
        foreach (string v in new[] { "DISPLAY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "XAUTHORITY", "XDG_RUNTIME_DIR" })
        {
            psi.Environment.Remove(v);
        }

        psi.Environment["DATA"] = SpikePaths.ClientData;
        psi.Environment["NET"] = "host";
        psi.Environment["TIMEOUT"] = timeoutSeconds.ToString(CultureInfo.InvariantCulture);
        psi.Environment["SHOTS"] = shotSeconds.ToString(CultureInfo.InvariantCulture);
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;

        Directory.CreateDirectory(Path.Combine(SpikePaths.SpikeRoot, "runs"));
        timeline.Mark("sandbox.launch", $"run={runName}");
        Process p = Process.Start(psi)!;
        return new SandboxClient(p, timeline, runDir);
    }

    /// <summary>Finds the Vintagestory process among the sandbox's descendants, by exact comm.</summary>
    public int? PollClientPid()
    {
        if (ClientPid is { } known)
        {
            return known;
        }

        foreach (int pid in Descendants(_sandbox.Id))
        {
            if (CommOf(pid) == "Vintagestory")
            {
                ClientPid = pid;
                _timeline.Mark("client.process", $"pid={pid}");
                return pid;
            }
        }

        return null;
    }

    public bool HasDescendant(string comm) => Descendants(_sandbox.Id).Any(pid => CommOf(pid) == comm);

    public bool ClientAlive => ClientPid is { } pid && Directory.Exists($"/proc/{pid}") && CommOf(pid) == "Vintagestory";

    public string? CoreLimitLine()
        => ClientPid is { } pid
            ? File.ReadAllLines($"/proc/{pid}/limits").FirstOrDefault(l => l.StartsWith("Max core file size", StringComparison.Ordinal))
            : null;

    public long? RssKb()
    {
        if (ClientPid is not { } pid)
        {
            return null;
        }

        string? line = File.ReadAllLines($"/proc/{pid}/status").FirstOrDefault(l => l.StartsWith("VmRSS:", StringComparison.Ordinal));
        return line == null ? null : long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
    }

    /// <summary>SIGTERM to the exact Vintagestory PID, after checking its comm, then waits for the
    /// sandbox to wind down.</summary>
    public async Task StopAsync(TimeSpan wait)
    {
        if (!_sandbox.HasExited && PollClientPid() is { } pid && CommOf(pid) == "Vintagestory")
        {
            _timeline.Mark("client.sigterm", $"pid={pid}");
            using Process kill = Process.Start("kill", ["-TERM", pid.ToString(CultureInfo.InvariantCulture)])!;
            await kill.WaitForExitAsync();
        }

        using var cts = new CancellationTokenSource(wait);
        try
        {
            await _sandbox.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _timeline.Mark("sandbox.still-running-after-stop");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The sandbox's own TIMEOUT is the safety net; nothing is killed by pattern here.
        if (!_sandbox.HasExited)
        {
            await StopAsync(TimeSpan.FromSeconds(30));
        }

        _sandbox.Dispose();
    }

    private static string? CommOf(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IEnumerable<int> Descendants(int root)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid))
            {
                continue;
            }

            try
            {
                string stat = File.ReadAllText(Path.Combine(dir, "stat"));
                int close = stat.LastIndexOf(')');
                string[] rest = stat[(close + 2)..].Split(' ');
                int ppid = int.Parse(rest[1], CultureInfo.InvariantCulture);
                if (!children.TryGetValue(ppid, out List<int>? list))
                {
                    children[ppid] = list = [];
                }

                list.Add(pid);
            }
            catch (IOException)
            {
                // The process ended between the listing and the read.
            }
        }

        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (children.TryGetValue(cur, out List<int>? kids))
            {
                foreach (int k in kids)
                {
                    yield return k;
                    queue.Enqueue(k);
                }
            }
        }
    }
}
