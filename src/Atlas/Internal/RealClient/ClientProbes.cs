using System.Diagnostics;

namespace Atlas.Internal.RealClient;

/// <summary>The facts the availability ladder reads from the machine, as delegates so every rung
/// is a pure test (the thin-shell convention of ADR 0005): the environment, the platform, a
/// <c>PATH</c> lookup, three file system reads and the user-namespace probe.
/// <see cref="OfThisMachine"/> is the real shell.</summary>
internal sealed record ClientProbes
{
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

    /// <summary>Tries to build the sandbox's namespaces with <c>unshare</c> (given by path) and
    /// run a throwaway command in them. Returns <see langword="null"/> when it worked, else the
    /// first line of what went wrong. Run only once <c>unshare</c> has been found.</summary>
    public required Func<string, string?> ProbeNamespaces { get; init; }

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
        ProbeNamespaces = ProbeNamespacesWith,
    };

    private static string? FindOnPath(string tool)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, tool))
            .FirstOrDefault(File.Exists);

    // The namespaces of the real launch, and the two mounts its inner script makes first: a few
    // milliseconds, and the only way to learn that a locked-down kernel, an AppArmor profile or a
    // container's syscall filter refuses them.
    private static string? ProbeNamespacesWith(string unshare)
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
        psi.ArgumentList.Add("mount -t tmpfs tmpfs /tmp");

        try
        {
            using Process probe = Process.Start(psi)!;
            probe.StandardInput.Close();
            Task<string> error = probe.StandardError.ReadToEndAsync();
            if (!probe.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                probe.Kill(entireProcessTree: true);
                return "the probe did not finish in 10 seconds";
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
}
