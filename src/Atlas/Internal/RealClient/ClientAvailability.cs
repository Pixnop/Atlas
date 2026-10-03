using System.Globalization;
using Atlas.Internal.Bootstrap;

namespace Atlas.Internal.RealClient;

/// <summary>Whether the real client can run here, decided before anything starts: one reason and
/// one remedy per case, in the order of the design's skip ladder (CI variable, the
/// <c>ATLAS_CLIENT=off</c> switch, platform, <c>unshare</c> and user namespaces, <c>Xvfb</c>, the
/// install's client files, the .NET runtime, the data path). The first rung that fails wins, and
/// the rungs after it are not even probed, so a CI run never starts a namespace probe. The
/// decision is pure over <see cref="ClientProbes"/>; the caller turns it into a skip or a
/// failure.</summary>
internal sealed record ClientAvailability
{
    /// <summary>The variable that switches the real client off when it is <c>off</c>.</summary>
    public const string SwitchVariable = "ATLAS_CLIENT";

    /// <summary>The client's executable (the .NET apphost) in the install.</summary>
    public const string ApphostName = "Vintagestory";

    /// <summary>Environment variables CI systems set, checked first: GitHub Actions, Azure
    /// Pipelines, GitLab and the generic one.</summary>
    public static readonly IReadOnlyList<string> CiVariables = ["CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI"];

    /// <summary>Files, relative to the install, without which the client cannot start. The
    /// server-only installs the engine tests often use have none of them.</summary>
    public static readonly IReadOnlyList<string> ClientFiles =
        [ApphostName, "Vintagestory.dll", DotnetRuntime.RuntimeConfigFileName, "Lib/libglfw.so.3"];

    private const string MissingClientFilesRemedy =
        $"Point {VsInstall.VariableName} at a full client install, the folder holding the {ApphostName} executable, and not at a server-only install.";

    /// <summary>Gets why the client cannot run, or <see cref="ClientUnavailableReason.None"/>.</summary>
    public ClientUnavailableReason Reason { get; private init; }

    /// <summary>Gets what is wrong, as a sentence.</summary>
    public string Message { get; private init; } = string.Empty;

    /// <summary>Gets what to do about it, as a sentence (empty only when the client can
    /// run).</summary>
    public string Remedy { get; private init; } = string.Empty;

    /// <summary>Gets the resolved toolchain, set exactly when the client can run.</summary>
    public ClientToolchain? Toolchain { get; private init; }

    /// <summary>Gets a value indicating whether the client can run here.</summary>
    public bool IsAvailable => Toolchain is not null;

    /// <summary>The text of a skip: what is wrong, then what to do about it.</summary>
    /// <returns>One line.</returns>
    public override string ToString()
        => IsAvailable ? "The real client can run here." : $"Real client unavailable: {Message} {Remedy}";

    /// <summary>Walks the ladder.</summary>
    /// <param name="installDirectory">The game install, or <see langword="null"/> to take the
    /// <c>VINTAGE_STORY</c> variable the rest of Atlas uses.</param>
    /// <param name="dataPath">The client data path, or <see langword="null"/> when none was
    /// given.</param>
    /// <param name="probes">The machine's facts.</param>
    /// <returns>The first failing rung, or an available result carrying the
    /// toolchain.</returns>
    public static ClientAvailability Check(string? installDirectory, string? dataPath, ClientProbes probes)
    {
        foreach (string variable in CiVariables)
        {
            if (IsSwitchedOn(probes.Env(variable)))
            {
                return No(
                    ClientUnavailableReason.CiEnvironment,
                    $"A CI environment was detected ({variable} is set).",
                    "The real client runs on developer machines only: a CI runner has no logged-in game session, and none is ever stored in a CI secret.");
            }
        }

        if (string.Equals(probes.Env(SwitchVariable)?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
        {
            return No(
                ClientUnavailableReason.SwitchedOff,
                $"{SwitchVariable} is set to off.",
                $"Unset {SwitchVariable}, or set it to anything but off, to run the real client.");
        }

        if (!probes.IsLinux)
        {
            return No(
                ClientUnavailableReason.NotLinux,
                "The real client sandbox needs Linux.",
                "On Windows, run the tests inside WSL2 with a separate Linux install of the game; macOS is not supported.");
        }

        if (probes.FindTool("unshare") is not { } unshare)
        {
            return No(
                ClientUnavailableReason.UnshareMissing,
                "unshare was not found on PATH.",
                "Install util-linux, which provides it.");
        }

        if (probes.ProbeNamespaces(unshare) is { } namespaceFailure)
        {
            return No(
                ClientUnavailableReason.UserNamespacesUnusable,
                $"unshare cannot build the sandbox's user, mount and PID namespaces here ({namespaceFailure}).",
                "Allow unprivileged user namespaces (sysctl kernel.unprivileged_userns_clone=1, or kernel.apparmor_restrict_unprivileged_userns=0 on Ubuntu 24.04), make sure bash and mount are installed, and inside a container allow the namespace system calls.");
        }

        if (probes.FindTool("Xvfb") is not { } xvfb)
        {
            return No(
                ClientUnavailableReason.XvfbMissing,
                "Xvfb was not found on PATH.",
                "Install it: xorg-server-xvfb (Arch), xvfb (Debian, Ubuntu) or xorg-x11-server-Xvfb (Fedora).");
        }

        string? install = installDirectory ?? probes.Env(VsInstall.VariableName);
        if (string.IsNullOrWhiteSpace(install))
        {
            return No(
                ClientUnavailableReason.InstallIncomplete,
                $"No game install was given and {VsInstall.VariableName} is not set.",
                MissingClientFilesRemedy);
        }

        string[] missing = [.. ClientFiles.Where(file => !probes.FileExists(Path.Combine(install, file)))];
        if (missing.Length > 0)
        {
            return No(
                ClientUnavailableReason.InstallIncomplete,
                $"The game install at '{install}' lacks client files: {string.Join(", ", missing)}.",
                MissingClientFilesRemedy);
        }

        RuntimeRequirement? requirement = DotnetRuntime.ReadRequirement(
            probes.ReadText(Path.Combine(install, DotnetRuntime.RuntimeConfigFileName)));
        if (requirement is null)
        {
            return No(
                ClientUnavailableReason.DotnetRuntimeMissing,
                $"{DotnetRuntime.RuntimeConfigFileName} in '{install}' does not say which .NET runtime the client needs.",
                "Reinstall the game: the client's runtime configuration is damaged.");
        }

        IReadOnlyList<string> candidates = DotnetRuntime.CandidateRoots(
            probes.Env("DOTNET_ROOT"), probes.ReadText(DotnetRuntime.RegisteredLocationFile));
        string? dotnetRoot = DotnetRuntime.FindRoot(
            requirement,
            candidates,
            root => probes.ListFolders(Path.Combine(root, "shared", requirement.Framework)));
        if (dotnetRoot is null)
        {
            return No(
                ClientUnavailableReason.DotnetRuntimeMissing,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The client needs {requirement.Framework} {requirement.Minimum} (or a later {requirement.Minimum.Major}.x) and none of the dotnet folders its launcher looks in has it ({string.Join(", ", candidates)})."),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Install the .NET {requirement.Minimum.Major} runtime, or set DOTNET_ROOT to the dotnet folder that holds it: the launcher does not search PATH."));
        }

        if (string.IsNullOrWhiteSpace(dataPath))
        {
            return No(
                ClientUnavailableReason.NoDataPath,
                "No client data path was given.",
                "Give a data path dedicated to the client tests. It is never the game's own data folder, and it must not be under /tmp, which the sandbox replaces.");
        }

        return new ClientAvailability
        {
            Toolchain = new ClientToolchain(unshare, xvfb, probes.FindTool("import"), install, dotnetRoot, dataPath),
        };
    }

    // A CI variable counts as set when it holds something other than the usual spellings of no.
    private static bool IsSwitchedOn(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Trim().Equals("0", StringComparison.Ordinal)
           && !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    private static ClientAvailability No(ClientUnavailableReason reason, string message, string remedy)
        => new() { Reason = reason, Message = message, Remedy = remedy };
}
