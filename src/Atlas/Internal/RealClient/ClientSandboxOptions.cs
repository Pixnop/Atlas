namespace Atlas.Internal.RealClient;

/// <summary>What one sandboxed client run is asked to do. The install, the dotnet folder and the
/// tools come from the <see cref="ClientToolchain"/> the availability check resolved; this is
/// the part the caller chooses.</summary>
internal sealed record ClientSandboxOptions
{
    /// <summary>Gets the folder of this run, which must not exist yet or must be empty: a run
    /// folder is never reused, so nothing in it is older than the run. Atlas creates it and
    /// everything the sandbox writes outside the client's data path lands in it (see
    /// <see cref="SandboxPlan"/> for the layout). It must not be under <c>/tmp</c>.</summary>
    public required string RunDirectory { get; init; }

    /// <summary>Gets the arguments after <c>--dataPath</c> and <c>--logPath</c>, which Atlas
    /// always passes itself: <c>--connect</c>, <c>--pw</c>, <c>--addModPath</c> and the like. A
    /// path in them is resolved inside the sandbox, where <c>/tmp</c> is empty.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the wall-clock ceiling, counted from the client's start: when it passes, the
    /// sandbox asks the client to stop, then kills it after <see cref="StopGrace"/>, whatever the
    /// caller is doing.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets how long the client gets to end after being asked to (SIGTERM) before it is
    /// killed (SIGKILL). A client in a world needs a few seconds to tear down under software
    /// rendering: 0.1 to 17 s were measured.</summary>
    public TimeSpan StopGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the time between two screenshots of the private display, or
    /// <see cref="TimeSpan.Zero"/> for none. Without ImageMagick's <c>import</c> there are none
    /// either way.</summary>
    public TimeSpan ScreenshotInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets a value indicating whether the sandbox gets a network namespace of its own,
    /// with nothing but a loopback of its own in it. False by default, because the client has to
    /// reach Atlas's listener on the host's loopback and the auth service; set it for a run that
    /// must reach nothing.</summary>
    public bool IsolateNetwork { get; init; }

    /// <summary>Gets the program to run in the sandbox in place of the install's client. A test
    /// seam: it lets the mechanics (environment, guardian, ceiling, stop ladder) be proven with a
    /// harmless program instead of a 3 GB client. It runs in the same sandbox, nothing else
    /// changes.</summary>
    internal string? ProgramOverride { get; init; }

    /// <summary>Checks the options before anything is created.</summary>
    /// <param name="toolchain">The resolved toolchain.</param>
    /// <param name="hostUid">The user id of this process, whose <c>/run/user</c> the sandbox
    /// hides.</param>
    /// <exception cref="ArgumentException">A path the sandbox would hide or a bound that makes no
    /// sense.</exception>
    public void Validate(ClientToolchain toolchain, uint hostUid)
    {
        if (Timeout < TimeSpan.FromSeconds(1) || StopGrace < TimeSpan.FromSeconds(1) || ScreenshotInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout and StopGrace must be at least one second, ScreenshotInterval not negative.");
        }

        foreach ((string name, string path) in new[] { ("run directory", RunDirectory), ("data path", toolchain.DataPath) })
        {
            if (HiddenBySandbox(path, hostUid) is { } hider)
            {
                throw new ArgumentException(
                    $"The {name} '{path}' is under {hider}, which the sandbox replaces with an empty folder: nothing written there would outlive the run.");
            }
        }

        if (Directory.Exists(RunDirectory) && Directory.EnumerateFileSystemEntries(RunDirectory).Any())
        {
            throw new ArgumentException($"The run directory '{RunDirectory}' is not empty: a run folder is never reused.");
        }
    }

    /// <summary>The folder, if any, that the sandbox mounts a private tmpfs over and that holds a
    /// path: such a path is invisible to the host once the client wrote to it.</summary>
    /// <param name="path">A path the client or Atlas will write to.</param>
    /// <param name="hostUid">The user id whose <c>/run/user</c> is hidden.</param>
    /// <returns>The hiding folder, or <see langword="null"/> when the path is safe.</returns>
    internal static string? HiddenBySandbox(string path, uint hostUid)
    {
        string full = Path.GetFullPath(path);
        return new[] { "/tmp", $"/run/user/{hostUid}" }
            .FirstOrDefault(hider => full == hider || full.StartsWith(hider + "/", StringComparison.Ordinal));
    }
}
