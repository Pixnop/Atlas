using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Atlas.Internal.RealClient;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>What the real-client tests share: whether this machine can run the client at all, a
/// fresh folder per test, and the polling helpers. Everything a run writes goes under
/// <see cref="Root"/>, never under <c>/tmp</c> (the sandbox replaces it), and every data path is
/// a folder this class creates new: a client is never started on a data path that has seen a
/// login.</summary>
internal static class RealClientEnvironment
{
    /// <summary>The variable that moves <see cref="Root"/>.</summary>
    public const string RootVariable = "ATLAS_CLIENT_TEST_ROOT";

    /// <summary>The variable naming the game install, as for the rest of the suite.</summary>
    public const string InstallVariable = "VINTAGE_STORY";

    /// <summary>The line a client on a data path with no login writes at its login screen.</summary>
    public const string LoginScreenMarker = "Cached session key is invalid, require login";

    private static readonly Lazy<ClientAvailability> AvailabilityOnce = new(
        () => ClientAvailability.Check(null, Root, ClientProbes.OfThisMachine()));

    private static readonly Lazy<(ClientAvailability? Failure, SandboxTools? Tools)> SandboxOnce = new(
        () =>
        {
            ClientAvailability? failure = ClientAvailability.CheckSandboxTools(ClientProbes.OfThisMachine(), out SandboxTools? tools);
            return (failure, tools);
        });

    /// <summary>Gets the folder every test run lives under: <c>ATLAS_CLIENT_TEST_ROOT</c> when
    /// set, else a folder next to this assembly. Runs are kept (they hold logs and screenshots
    /// worth reading after a red run); delete the folder to clean up.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(TestPaths.OwnOutputDirectory, "real-client-runs");

    /// <summary>Gets the availability of the client on this machine, decided once.</summary>
    public static ClientAvailability Availability => AvailabilityOnce.Value;

    /// <summary>Gets why the sandbox itself cannot run here (the rungs of the ladder that do not
    /// concern the game install), or <see langword="null"/> when it can, decided once.</summary>
    public static ClientAvailability? SandboxFailure => SandboxOnce.Value.Failure;

    /// <summary>Gets the sandbox's programs on this machine; only valid when
    /// <see cref="SandboxFailure"/> is <see langword="null"/>.</summary>
    public static SandboxTools SandboxPrograms => SandboxOnce.Value.Tools!;

    /// <summary>The toolchain of a run that starts a stand-in program: the real sandbox tools, and
    /// a folder that exists for the install, which the inner script only enters. No game install
    /// is read.</summary>
    /// <param name="tools">The sandbox's programs.</param>
    /// <param name="installStandIn">An existing folder.</param>
    /// <param name="dataPath">The fresh data path.</param>
    /// <returns>The toolchain.</returns>
    public static ClientToolchain StandInToolchain(SandboxTools tools, string installStandIn, string dataPath)
        => new(tools.UnsharePath, tools.SetprivPath, tools.XvfbPath, tools.ImportPath, installStandIn, installStandIn, dataPath);

    /// <summary>Makes the folder of one test: a new run folder name and a new, empty data path
    /// that the test then hands to the sandbox.</summary>
    /// <param name="name">The test's name.</param>
    /// <returns>The folders.</returns>
    public static TestRun NewRun(string name)
    {
        string folder = Path.Combine(
            Root,
            string.Create(CultureInfo.InvariantCulture, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"));
        Directory.CreateDirectory(folder);
        return new TestRun(folder);
    }

    /// <summary>Polls until a condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="timeout">How long to wait at most.</param>
    /// <param name="what">What was waited for, for the failure message.</param>
    /// <param name="abortIf">A check that ends the wait early with its own explanation, such as
    /// the sandbox having exited.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task EventuallyAsync(Func<bool> condition, TimeSpan timeout, string what, Func<string?>? abortIf = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (abortIf?.Invoke() is { } reason)
            {
                throw new Xunit.Sdk.XunitException($"Gave up waiting for {what}: {reason}");
            }

            if (clock.Elapsed > timeout)
            {
                throw new Xunit.Sdk.XunitException($"Timed out after {timeout.TotalSeconds:0} s waiting for {what}.");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>The names in the host's X11 socket folder, which a sandboxed client must never
    /// change or reach.</summary>
    /// <returns>The sorted entries, empty when the folder does not exist.</returns>
    public static string[] HostX11Sockets()
        => Directory.Exists("/tmp/.X11-unix")
            ? [.. Directory.EnumerateFileSystemEntries("/tmp/.X11-unix").Select(Path.GetFileName).Order(StringComparer.Ordinal)!]
            : [];

    /// <summary>The folders of one test.</summary>
    /// <param name="Folder">The test's own folder.</param>
    internal sealed record TestRun(string Folder)
    {
        /// <summary>Gets the run folder the sandbox creates (it must not exist yet).</summary>
        public string RunDirectory => Path.Combine(Folder, "run");

        /// <summary>Gets the fresh client data path: it does not exist until the client makes it,
        /// so it has never held a login.</summary>
        public string DataPath => Path.Combine(Folder, "data");

        /// <summary>Gets the toolchain of a stand-in run in this folder.</summary>
        public ClientToolchain StandInToolchain => RealClientEnvironment.StandInToolchain(SandboxPrograms, Folder, DataPath);

        /// <summary>Gets the options of a run in this folder.</summary>
        /// <param name="program">A stand-in program, or <see langword="null"/> for the real
        /// client.</param>
        /// <returns>The options.</returns>
        public ClientSandboxOptions Options(string? program = null)
            => new() { RunDirectory = RunDirectory, ProgramOverride = program };

        /// <summary>Writes a stand-in program next to the run folder.</summary>
        /// <param name="body">The shell script after the shebang.</param>
        /// <returns>The path of the executable script.</returns>
        [UnsupportedOSPlatform("windows")]
        public string StandIn(string body)
        {
            string path = Path.Combine(Folder, "stand-in.sh");
            File.WriteAllText(path, "#!/bin/bash\n" + body + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }
}
