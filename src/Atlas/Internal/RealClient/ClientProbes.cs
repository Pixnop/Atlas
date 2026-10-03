using System.Diagnostics;
using System.Globalization;

namespace Atlas.Internal.RealClient;

/// <summary>The facts the availability ladder reads from the machine, as delegates so every rung
/// is a pure test (the thin-shell convention of ADR 0005): the environment, the platform, a
/// <c>PATH</c> lookup, three file system reads and the user-namespace probe.
/// <see cref="OfThisMachine"/> is the real shell.</summary>
internal sealed record ClientProbes
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Reads an environment variable; <see langword="null"/> when unset.</summary>
    public required Func<string, string?> Env { get; init; }

    /// <summary>Whether this is Linux, the only platform the sandbox exists on.</summary>
    public required bool IsLinux { get; init; }

    /// <summary>Finds a program on <c>PATH</c>: its full path, or <see langword="null"/>.</summary>
    public required Func<string, string?> FindTool { get; init; }

    /// <summary>Whether a file exists.</summary>
    public required Func<string, bool> FileExists { get; init; }

    /// <summary>Reads a text file; <see langword="null"/> when it is missing or unreadable.</summary>
    public required Func<string, string?> ReadText { get; init; }

    /// <summary>Lists the names of the sub-folders of a folder; empty when it is missing.</summary>
    public required Func<string, IEnumerable<string>> ListFolders { get; init; }

    /// <summary>Tries to build the sandbox's namespaces with <c>unshare</c> (the first argument,
    /// by path), mount a tmpfs in them and empty the capabilities with <c>setpriv</c> (the
    /// second), as the real launch does. Returns <see langword="null"/> when it worked, else the
    /// first line of what went wrong. Run only once both tools have been found.</summary>
    public required Func<string, string, string?> ProbeNamespaces { get; init; }

    /// <summary>The probes of the machine this process runs on.</summary>
    /// <returns>The real probes.</returns>
    public static ClientProbes OfThisMachine() => new()
    {
        Env = Environment.GetEnvironmentVariable,
        IsLinux = OperatingSystem.IsLinux(),
        FindTool = FindOnPath,
        FileExists = File.Exists,
        ReadText = path =>
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        },
        ListFolders = path => Directory.Exists(path)
            ? Directory.EnumerateDirectories(path).Select(dir => Path.GetFileName(dir))
            : [],
        ProbeNamespaces = (unshare, setpriv) => ProbeNamespacesWith(unshare, setpriv, ProbeTimeout),
    };

    /// <summary>Tries the namespaces of the real launch, the first mount its inner script makes and
    /// the capability drop it ends with: a few milliseconds, and the only way to learn that a
    /// locked-down kernel, an AppArmor profile or a container's syscall filter refuses them. The
    /// timeout is a parameter for the pure tests, which stand in a fake <c>unshare</c> that never
    /// ends.</summary>
    /// <param name="unshare">The <c>unshare</c> program.</param>
    /// <param name="setpriv">The <c>setpriv</c> program.</param>
    /// <param name="timeout">How long to wait for the probe to end before killing it.</param>
    /// <returns><see langword="null"/> when it worked, else the first line of what went wrong.</returns>
    internal static string? ProbeNamespacesWith(string unshare, string setpriv, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(unshare)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (string flag in SandboxPlan.NamespaceFlags(isolateNetwork: false))
        {
            psi.ArgumentList.Add(flag);
        }

        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"mount -t tmpfs tmpfs /tmp && exec \"$0\" {string.Join(' ', SandboxPlan.CapabilityDropFlags)} true");
        psi.ArgumentList.Add(setpriv);

        try
        {
            using Process probe = Process.Start(psi)!;
            probe.StandardInput.Close();
            Task<string> error = probe.StandardError.ReadToEndAsync();
            if (!probe.WaitForExit(timeout))
            {
                probe.Kill(entireProcessTree: true);
                return $"the probe did not finish in {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds";
            }

            return probe.ExitCode == 0
                ? null
                : error.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ?? $"the probe exited with code {probe.ExitCode}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }

    private static string? FindOnPath(string tool)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, tool))
            .FirstOrDefault(File.Exists);
}
