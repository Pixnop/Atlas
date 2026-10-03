using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Atlas.Internal.RealClient;

// Design. The stock game client is a 3 GB GUI program that opens a window, can reach the session
// bus and, on a developer's machine, holds a logged-in game session. It runs here and nowhere
// else, so that it cannot touch the developer's desktop and cannot outlive the test that started it.
//
// Guarantees:
//  - A hidden desktop. The client runs in its own user, mount and PID namespaces
//    (SandboxPlan.NamespaceFlags). Inside, /tmp (the host's X11 sockets) and /run/user/<uid> (the
//    session bus, Wayland and audio sockets) are empty tmpfs mounts, the display is a private Xvfb,
//    and the client's environment is built from nothing plus a whitelist
//    (SandboxPlan.BuildClientEnvironment): never the host's DISPLAY, WAYLAND_DISPLAY,
//    DBUS_SESSION_BUS_ADDRESS or XAUTHORITY. The inner script refuses to start the client when a
//    mount fails or the display is not the one its own Xvfb made.
//  - No core dump. RLIMIT_CORE is 0 for everything inside: a core file of a logged-in client would
//    hold its session key.
//  - A lifetime tied to the host. The host holds the write end of a pipe that is the sandbox's
//    stdin, and the inner script's guardian reads it. At EOF, which the kernel delivers when the
//    host closes the pipe or dies (SIGKILL included), the guardian sends the client SIGTERM and
//    SIGKILL after StopGrace. A PID namespace alone does not do this: its first process outlives a
//    killed parent. When the client ends, the script ends and the kernel kills the rest of the
//    namespace. Two more layers sit behind that: a wall-clock ceiling inside the sandbox
//    (ClientSandboxOptions.Timeout), and --kill-child, which makes one SIGKILL on the launcher take
//    the namespace down.
//  - A bounded stop. StopAsync closes the pipe, waits StopGrace plus a margin, then kills the launcher.
//
// Limits:
//  - It hides the desktop, not the files. Outside /tmp and /run/user/<uid> the file system is the
//    host's, readable as the developer's user (root only inside the user namespace, which gives no
//    privilege outside). A run folder or data path under /tmp is refused, since it would be hidden.
//  - Abstract sockets belong to the network namespace, not the mount namespace. The default sandbox
//    shares the host's network (the client has to reach Atlas's loopback listener), so it shares
//    the host's abstract X11 sockets too. What keeps the client off the host display is then the
//    empty environment and the private Xvfb taking a free display number, not a namespace. The
//    private Xvfb also has no X authority cookie, so a process of the host can connect to it (a
//    host-side screenshot of it worked in a check) and read what the client shows.
//    ClientSandboxOptions.IsolateNetwork closes both for a run that needs no network.
//  - It needs unprivileged user namespaces (ClientAvailability checks) and it is Linux only.

/// <summary>One stock game client running in its sandbox: the shell around
/// <see cref="SandboxPlan"/>. It creates the run folder, starts the launcher with the pipe the
/// guardian watches, reports how the sandbox ended, and stops it on request with a bounded
/// SIGTERM then SIGKILL ladder. Start it only from an available
/// <see cref="ClientAvailability"/>.</summary>
[UnsupportedOSPlatform("windows")]
internal sealed class ClientSandbox : IAsyncDisposable
{
    // A stop is the guardian's SIGTERM, StopGrace later its SIGKILL, and then a moment for the
    // namespace to wind down: this is that moment, on top of StopGrace.
    private static readonly TimeSpan StopMargin = TimeSpan.FromSeconds(5);

    private readonly Process _launcher;
    private readonly TimeSpan _stopGrace;
    private int _stopRequested;

    private ClientSandbox(Process launcher, SandboxPlan plan, TimeSpan stopGrace)
    {
        _launcher = launcher;
        Plan = plan;
        _stopGrace = stopGrace;
    }

    /// <summary>Gets the plan this sandbox was started from: the run folder and what is in it.</summary>
    public SandboxPlan Plan { get; }

    /// <summary>Gets the process id of the <c>unshare</c> launcher, as the host sees it. The
    /// processes of the sandbox are its descendants.</summary>
    public int LauncherPid => _launcher.Id;

    /// <summary>Gets a value indicating whether the sandbox has ended, which is when the client
    /// has: nothing in it outlives the client.</summary>
    public bool HasExited => _launcher.HasExited;

    /// <summary>Gets the exit code of the sandbox, which is the client's (124 or 143 after a
    /// stop). It says little: the client exits 1, 139 or 0 for crashes and stops alike. Only valid
    /// once <see cref="HasExited"/>.</summary>
    public int ExitCode => _launcher.ExitCode;

    /// <summary>Gets a value indicating whether a stop was requested. A crash detector reading
    /// the client's logs must ignore everything the client writes once this is true: a client
    /// asked to stop can end with a segmentation fault or log a crash that is only its
    /// exit.</summary>
    public bool StopRequested => Volatile.Read(ref _stopRequested) == 1;

    /// <summary>Starts the sandbox: validates the options, creates the run folder (owner-only)
    /// and its sub-folders, and launches the inner script, which starts the private display and
    /// then the client.</summary>
    /// <param name="options">What to run.</param>
    /// <param name="toolchain">The resolved toolchain, from an available
    /// <see cref="ClientAvailability"/>.</param>
    /// <returns>The running sandbox.</returns>
    /// <exception cref="ArgumentException">The options are refused (see
    /// <see cref="ClientSandboxOptions.Validate"/>).</exception>
    public static ClientSandbox Start(ClientSandboxOptions options, ClientToolchain toolchain)
    {
        uint uid = HostUid();
        options.Validate(toolchain, uid);
        SandboxPlan plan = SandboxPlan.Create(
            options, toolchain, uid, Environment.GetEnvironmentVariable("PATH"), SandboxPlan.LoadInnerScript());

        foreach ((string path, bool ownerOnly) in plan.Folders())
        {
            Directory.CreateDirectory(path);
            if (ownerOnly)
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var psi = new ProcessStartInfo(plan.FileName) { UseShellExecute = false, RedirectStandardInput = true };
        foreach (string argument in plan.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        // Nothing of this process's environment reaches the sandbox: not even the desktop
        // variables a test host has.
        psi.Environment.Clear();
        foreach ((string name, string value) in plan.LauncherEnvironment)
        {
            psi.Environment[name] = value;
        }

        return new ClientSandbox(Process.Start(psi)!, plan, options.StopGrace);
    }

    /// <summary>Waits for the sandbox to end.</summary>
    /// <param name="cancellationToken">Cancels the wait, not the sandbox.</param>
    /// <returns>A task that completes when the client and everything in the sandbox are
    /// gone.</returns>
    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => _launcher.WaitForExitAsync(cancellationToken);

    /// <summary>Stops the client and the sandbox: closes the guardian's pipe, which makes the
    /// inner script send SIGTERM and, after the grace period, SIGKILL; waits that long plus a
    /// margin; and if the sandbox is still there, kills the launcher, which takes the namespace
    /// with it. Safe to call again.</summary>
    /// <returns>A task that completes when nothing of the sandbox is left.</returns>
    public async Task StopAsync()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        if (_launcher.HasExited)
        {
            return;
        }

        try
        {
            _launcher.StandardInput.Close();
        }
        catch (IOException)
        {
            // The pipe is already broken, which is the same EOF for the guardian.
        }

        if (!await ExitsWithin(_stopGrace + StopMargin))
        {
            _launcher.Kill();
            await ExitsWithin(StopMargin);
        }
    }

    /// <summary>Stops the sandbox (see <see cref="StopAsync"/>) and releases the process
    /// handle.</summary>
    /// <returns>A task that completes when the sandbox is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _launcher.Dispose();
    }

    private static uint HostUid()
    {
        // The real user id, the first number of the Uid line.
        string line = File.ReadLines("/proc/self/status").First(l => l.StartsWith("Uid:", StringComparison.Ordinal));
        return uint.Parse(line.Split('\t', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
    }

    private async Task<bool> ExitsWithin(TimeSpan bound)
    {
        using var cts = new CancellationTokenSource(bound);
        try
        {
            await _launcher.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
