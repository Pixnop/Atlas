using System.Globalization;

namespace Atlas.Internal.RealClient;

/// <summary>The pure decision core of a sandboxed client launch: the program and arguments that
/// start the sandbox, the environment of that launcher, the whitelisted environment of the
/// client, and the folders of the run. No IO, so the guarantees that live in the arguments (which
/// namespaces, which variables, which paths) are pure tests; <see cref="ClientSandbox"/> is the
/// shell that creates the folders and starts the process.</summary>
/// <remarks>The run folder layout: <c>logs/</c> (the client's <c>--logPath</c>: its
/// <c>client-main.log</c>, <c>client-crash.log</c>), <c>shots/</c> (screenshots of the private
/// display, when ImageMagick's <c>import</c> is installed), <c>home/</c>, <c>tmp/</c> and
/// <c>xdg/</c> (the client's private <c>HOME</c>, <c>TMPDIR</c> and <c>XDG_RUNTIME_DIR</c>),
/// <c>client.stdout</c> (its standard output and error), <c>sandbox.log</c> (what the inner script
/// did, with times), <c>launcher.log</c> (what <c>unshare</c> itself said), <c>xvfb.log</c> and
/// <c>display</c> (the private display number).</remarks>
internal sealed record SandboxPlan
{
    /// <summary>The name of the embedded inner script.</summary>
    public const string InnerScriptResource = "Atlas.Internal.RealClient.sandbox-inner.sh";

    /// <summary>The <c>PATH</c> the client gets: fixed, so a run does not depend on whose shell
    /// started it.</summary>
    public const string ClientPath = "/usr/local/bin:/usr/bin:/bin";

    /// <summary>Gets the program that starts the sandbox (a shell, which redirects the
    /// launcher's own output to <c>launcher.log</c> and replaces itself with
    /// <c>unshare</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the arguments of <see cref="FileName"/>.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Gets the whole environment of the launcher, and so of the inner script:
    /// <c>PATH</c> and the <c>ATLAS_SB_*</c> settings, nothing else. Notably none of the
    /// desktop's display, Wayland, session bus or X authority variables.</summary>
    public required IReadOnlyDictionary<string, string> LauncherEnvironment { get; init; }

    /// <summary>Gets the client's environment, handed to <c>env -i</c> by the inner script: the
    /// whole list but <c>DISPLAY</c>, whose value exists only once the private Xvfb runs.</summary>
    public required IReadOnlyDictionary<string, string> ClientEnvironment { get; init; }

    /// <summary>Gets the run folder.</summary>
    public required string RunDirectory { get; init; }

    /// <summary>Gets the client's <c>--logPath</c>.</summary>
    public string Logs => Path.Combine(RunDirectory, "logs");

    /// <summary>Gets the folder of the screenshots.</summary>
    public string Screenshots => Path.Combine(RunDirectory, "shots");

    /// <summary>Gets the private <c>HOME</c>.</summary>
    public string Home => Path.Combine(RunDirectory, "home");

    /// <summary>Gets the private <c>TMPDIR</c>, where the client's single-instance pipe lands.</summary>
    public string Tmp => Path.Combine(RunDirectory, "tmp");

    /// <summary>Gets the private <c>XDG_RUNTIME_DIR</c>, created owner-only.</summary>
    public string Xdg => Path.Combine(RunDirectory, "xdg");

    /// <summary>Gets the file the inner script logs to.</summary>
    public string SandboxLog => Path.Combine(RunDirectory, "sandbox.log");

    /// <summary>Gets the file the client's output goes to.</summary>
    public string ClientStdout => Path.Combine(RunDirectory, "client.stdout");

    /// <summary>The namespaces of the sandbox, as <c>unshare</c> flags: user (mapped to root
    /// inside), mount, PID with its own <c>/proc</c>, and the network one only when asked.
    /// <c>--kill-child</c> makes the namespace's first process die with <c>unshare</c>, so
    /// killing that one process takes everything down.</summary>
    /// <param name="isolateNetwork">Whether to add a network namespace.</param>
    /// <returns>The flags.</returns>
    public static IReadOnlyList<string> NamespaceFlags(bool isolateNetwork)
    {
        string[] flags = ["--user", "--map-root-user", "--mount", "--pid", "--fork", "--kill-child", "--mount-proc"];
        return isolateNetwork ? [.. flags, "--net"] : flags;
    }

    /// <summary>The client's environment: empty, plus this list. Private <c>HOME</c>,
    /// <c>TMPDIR</c> and <c>XDG_RUNTIME_DIR</c> inside the run folder, the fixed
    /// <see cref="ClientPath"/>, the dotnet folder the apphost needs, software rendering, no audio
    /// device, X11 rather than Wayland, the fonts of the install and a UTF-8 locale.</summary>
    /// <param name="runDirectory">The run folder.</param>
    /// <param name="toolchain">The resolved toolchain.</param>
    /// <returns>The variables, without <c>DISPLAY</c>.</returns>
    public static IReadOnlyDictionary<string, string> BuildClientEnvironment(string runDirectory, ClientToolchain toolchain)
        => new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = Path.Combine(runDirectory, "home"),
            ["TMPDIR"] = Path.Combine(runDirectory, "tmp"),
            ["XDG_RUNTIME_DIR"] = Path.Combine(runDirectory, "xdg"),
            ["PATH"] = ClientPath,
            ["DOTNET_ROOT"] = toolchain.DotnetRoot,
            ["LIBGL_ALWAYS_SOFTWARE"] = "1",
            ["ALSOFT_DRIVERS"] = "null",
            ["OPENTK_4_USE_WAYLAND"] = "0",
            ["FONTCONFIG_FILE"] = Path.Combine(toolchain.InstallDirectory, "fonts.conf"),
            ["LANG"] = "C.UTF-8",
        };

    /// <summary>Builds the plan.</summary>
    /// <param name="options">What the caller asked for.</param>
    /// <param name="toolchain">The resolved toolchain.</param>
    /// <param name="hostUid">The user id of this process.</param>
    /// <param name="hostPath">The <c>PATH</c> of this process, which the launcher needs to find
    /// <c>bash</c>, <c>mount</c>, <c>timeout</c> and the rest of the inner script's tools (the
    /// client does not get it).</param>
    /// <param name="innerScript">The text of the inner script.</param>
    /// <returns>The plan.</returns>
    public static SandboxPlan Create(
        ClientSandboxOptions options, ClientToolchain toolchain, uint hostUid, string? hostPath, string innerScript)
    {
        string run = Path.GetFullPath(options.RunDirectory);
        IReadOnlyDictionary<string, string> clientEnvironment = BuildClientEnvironment(run, toolchain);
        string program = options.ProgramOverride ?? toolchain.ClientProgram;
        string shots = toolchain.ImportPath is null ? "0" : Seconds(options.ScreenshotInterval);

        List<string> args =
        [
            "-c", "exec \"$@\" >\"$0\" 2>&1", Path.Combine(run, "launcher.log"),
            toolchain.UnsharePath,
            .. NamespaceFlags(options.IsolateNetwork),
            "bash", "-c", innerScript, "atlas-sandbox",
            .. clientEnvironment.Select(pair => $"{pair.Key}={pair.Value}"),
            "--",
            "--dataPath", toolchain.DataPath,
            "--logPath", Path.Combine(run, "logs"),
            .. options.Arguments,
        ];

        return new SandboxPlan
        {
            FileName = "/bin/sh",
            Arguments = args,
            RunDirectory = run,
            ClientEnvironment = clientEnvironment,
            LauncherEnvironment = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = string.IsNullOrEmpty(hostPath) ? ClientPath : hostPath,
                ["ATLAS_SB_RUN"] = run,
                ["ATLAS_SB_INSTALL"] = toolchain.InstallDirectory,
                ["ATLAS_SB_PROGRAM"] = program,
                ["ATLAS_SB_XVFB"] = toolchain.XvfbPath,
                ["ATLAS_SB_IMPORT"] = toolchain.ImportPath ?? string.Empty,
                ["ATLAS_SB_UID"] = hostUid.ToString(CultureInfo.InvariantCulture),
                ["ATLAS_SB_TIMEOUT"] = Seconds(options.Timeout),
                ["ATLAS_SB_KILL_AFTER"] = Seconds(options.StopGrace),
                ["ATLAS_SB_SHOTS"] = shots,
                ["ATLAS_SB_ISOLATE_NET"] = options.IsolateNetwork ? "1" : "0",
            },
        };
    }

    /// <summary>Reads the inner script embedded in this assembly.</summary>
    /// <returns>The script text.</returns>
    public static string LoadInnerScript()
    {
        using Stream stream = typeof(SandboxPlan).Assembly.GetManifestResourceStream(InnerScriptResource)
            ?? throw new InvalidOperationException($"The embedded resource {InnerScriptResource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The folders a run needs, each with whether it must be owner-only.</summary>
    /// <returns>The folders to create.</returns>
    public IEnumerable<(string Path, bool OwnerOnly)> Folders()
        => [(RunDirectory, true), (Logs, false), (Screenshots, false), (Home, false), (Tmp, false), (Xdg, true)];

    private static string Seconds(TimeSpan span)
        => ((long)Math.Ceiling(span.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
