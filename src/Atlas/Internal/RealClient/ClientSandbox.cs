using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Atlas.Internal.RealClient;

// Design. The stock game client is a 3 GB GUI program that opens a window, can reach the session
// bus and, on a developer's machine, holds a logged-in game session. It runs here and nowhere
// else, so that it cannot touch the developer's desktop and cannot outlive the test that started it.
//
// Guarantees:
//  - A hidden desktop. The client runs in its own user, mount, IPC and PID namespaces
//    (SandboxPlan.NamespaceFlags). Inside, /tmp (the host's X11 sockets), /dev/shm (the buffers
//    the desktop's programs share: browser and game surfaces) and /run/user/<uid> (the session
//    bus, Wayland and audio sockets, the X authority cookie of a Wayland session) are empty
//    tmpfs mounts, the IPC namespace leaves out the host's SysV shared memory (the segments X11
//    MIT-SHM images live in), semaphores and message queues, the display is a private Xvfb, and
//    the client's environment is built from nothing plus a whitelist
//    (SandboxPlan.BuildClientEnvironment): never the host's DISPLAY, WAYLAND_DISPLAY,
//    DBUS_SESSION_BUS_ADDRESS or XAUTHORITY. The inner script refuses to start the client when a
//    mount fails, when the capabilities cannot be dropped or when the display is not the one its
//    own Xvfb made.
//  - No working folder of the host's. The launcher starts in the run folder, and the inner
//    script's first act is to enter it. Every process of the sandbox inherits that one (the
//    client's own is the install folder): were it a folder under the host's /tmp or /run/user,
//    /proc/<pid>/cwd would open it past the private mounts. A mechanics test reads the working
//    folder of every process in the sandbox, with the test host started under /tmp.
//  - The mounts cannot be undone from inside. The user namespace maps the host user to uid 0, and
//    root of a user namespace owns its mount namespace: left with its capabilities, the client
//    (or any mod or other code running in its process) could simply umount /tmp and
//    /run/user/<uid> and see what they hide, which was measured. So the inner script runs in two
//    stages: the mounts with the namespace's capabilities, then it starts itself again through
//    setpriv with every capability set emptied and no_new_privs set (SandboxPlan.CapabilityDropFlags),
//    and checks /proc/self/status before it goes on. Xvfb, the screenshots and the client all
//    inherit that: nothing in the sandbox holds a capability while the client runs. umount then
//    fails with "must be superuser", and a nested user or mount namespace gets nothing back
//    (the nested mounts are locked). Mechanics tests prove this from a stand-in program.
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
//    host's, readable and writable as the developer's user (uid 0 inside the namespace, with no
//    capability, which gives no privilege outside either). A run folder or data path under /tmp is
//    refused, since it would be hidden, and so is the game's own data folder. The checks compare
//    the paths as written, with no symbolic link followed, so a link that leads into /tmp or into
//    the game's data folder gets through. Arguments that move the data or log path are refused.
//  - Abstract sockets belong to the network namespace, not the mount namespace. The default sandbox
//    shares the host's network (the client has to reach Atlas's loopback listener), so it shares
//    the host's abstract X11 sockets too. What keeps the client off the host display is then the
//    empty environment and the private Xvfb taking a free display number (it skips the ones the
//    host uses), not a namespace. On a desktop that lets any local process in by user id (Xwayland
//    on GNOME) or that keeps a ~/.Xauthority, the empty environment is the only guard. The private
//    Xvfb has no X authority cookie either, and abstract sockets have no file permissions, so any
//    local user, not only the owner, can connect to it (a host-side screenshot of it worked in a
//    check), read what the client shows and send it input.
//    ClientSandboxOptions.IsolateNetwork closes both for a run that needs no network.
//  - The sandbox is not a device or service jail: /dev/dri, /dev/snd and the system bus socket
//    (/run/dbus/system_bus_socket) are still there. None of them is the desktop.
//  - Arguments are visible to every local user: the client takes them on its command line, and
//    so do sh, unshare, bash, setpriv, env and timeout on the way, so --pw and the like can be read
//    from /proc/<pid>/cmdline for as long as the run lasts. A password given here must be good for
//    that run only. A crash outlives the run in one place: where the kernel's core pattern pipes
//    to systemd-coredump, RLIMIT_CORE 0 stops the core file but the crash still gets a journal
//    entry with the process's metadata, among it COREDUMP_CMDLINE (measured for a process killed
//    with SIGSEGV in the same namespaces), so the same password sits in the persistent journal
//    until it is vacuumed.
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
    private static readonly TimeSpan DefaultStopMargin = TimeSpan.FromSeconds(5);

    private readonly Process _launcher;
    private readonly TimeSpan _stopGrace;
    private readonly TimeSpan _stopMargin;
    private int _stopRequested;

    /// <summary>Initializes a new instance of the <see cref="ClientSandbox"/> class around a launcher
    /// that is already running. <see cref="Start"/> is the way in; this is internal for the pure
    /// tests of the stop ladder, which adopt a harmless process in place of the launcher and a short
    /// margin in place of the five seconds.</summary>
    /// <param name="launcher">The running launcher.</param>
    /// <param name="plan">The plan it was started from.</param>
    /// <param name="stopGrace">The time between the guardian's SIGTERM and its SIGKILL.</param>
    /// <param name="stopMargin">The time the namespace gets to wind down on top of the grace, or
    /// <see langword="null"/> for five seconds.</param>
    internal ClientSandbox(Process launcher, SandboxPlan plan, TimeSpan stopGrace, TimeSpan? stopMargin = null)
    {
        _launcher = launcher;
        Plan = plan;
        _stopGrace = stopGrace;
        _stopMargin = stopMargin ?? DefaultStopMargin;
    }

    /// <summary>Gets the plan this sandbox was started from: the run folder and what is in it.</summary>
    public SandboxPlan Plan { get; }

    /// <summary>Gets the process id of the <c>unshare</c> launcher, as the host sees it. The
    /// processes of the sandbox are its descendants.</summary>
    public int LauncherPid => _launcher.Id;

    /// <summary>Gets a value indicating whether the sandbox has ended, which is when the client
    /// has: nothing in it outlives the client.</summary>
    public bool HasExited => _launcher.HasExited;

    /// <summary>Gets the exit code of the sandbox, which is the client's: 124 after the ceiling,
    /// 143 or 137 after a stop that needed SIGTERM or SIGKILL, and 70 when the inner script
    /// refused to start the client. It says little otherwise: the client exits 1, 139 or 0 for
    /// crashes and stops alike. Only valid once <see cref="HasExited"/>.</summary>
    public int ExitCode => _launcher.ExitCode;

    /// <summary>Gets a value indicating whether a stop was requested. A crash detector reading
    /// the client's logs must ignore everything the client writes once this is true: a client
    /// asked to stop can end with a segmentation fault or log a crash that is only its
    /// exit.</summary>
    public bool StopRequested => Volatile.Read(ref _stopRequested) == 1;

    /// <summary>Starts the sandbox: validates the options, creates the run folder (owner-only)
    /// and its sub-folders, writes the inner script into it and launches that script, which sets
    /// up the private mounts, drops its capabilities, starts the private display and then the
    /// client.</summary>
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
        SandboxPlan plan = SandboxPlan.Create(options, toolchain, uid, Environment.GetEnvironmentVariable("PATH"));
        PrepareRunFolder(plan);
        return new ClientSandbox(Process.Start(LauncherStartInfo(plan))!, plan, options.StopGrace);
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

        if (!await ExitsWithin(_stopGrace + _stopMargin))
        {
            _launcher.Kill();
            await ExitsWithin(_stopMargin);
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

    /// <summary>Creates the run folder and its sub-folders (the run folder and the client's
    /// runtime folder owner-only, as the plan says) and writes the inner script into the run
    /// folder.</summary>
    /// <param name="plan">The plan to lay out.</param>
    internal static void PrepareRunFolder(SandboxPlan plan)
    {
        foreach ((string path, bool ownerOnly) in plan.Folders())
        {
            Directory.CreateDirectory(path);
            if (ownerOnly)
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        File.WriteAllText(plan.InnerScriptFile, SandboxPlan.LoadInnerScript());
    }

    /// <summary>The launcher's start information: the plan's program and arguments, the run folder
    /// as working folder, and an environment that is the plan's and nothing else.</summary>
    /// <param name="plan">The plan to launch.</param>
    /// <returns>The start information, not started.</returns>
    internal static ProcessStartInfo LauncherStartInfo(SandboxPlan plan)
    {
        // The run folder is the launcher's working folder, and so the working folder of whatever the
        // sandbox starts before the client's: the test host's own may lie under the /tmp or
        // /run/user/<uid> that the sandbox hides, and would stay reachable through /proc/<pid>/cwd.
        var psi = new ProcessStartInfo(plan.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            WorkingDirectory = plan.RunDirectory,
        };
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

        return psi;
    }

    /// <summary>The real user id in the lines of a <c>/proc/&lt;pid&gt;/status</c>: the first number
    /// of the <c>Uid:</c> line, which lists the real, effective, saved and file system ids.</summary>
    /// <param name="statusLines">The lines of the status file.</param>
    /// <returns>The real user id.</returns>
    internal static uint UidOf(IEnumerable<string> statusLines)
    {
        string line = statusLines.First(l => l.StartsWith("Uid:", StringComparison.Ordinal));
        return uint.Parse(line.Split('\t', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
    }

    private static uint HostUid() => UidOf(File.ReadLines("/proc/self/status"));

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
