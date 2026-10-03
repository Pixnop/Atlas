namespace Atlas.ClientSpike;

internal static class SpikePaths
{
    public static string OwnOutputDirectory { get; } =
        Path.GetDirectoryName(typeof(SpikePaths).Assembly.Location)!;

    public static readonly string SpikeRoot = Environment.GetEnvironmentVariable("ATLAS_SPIKE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev/.scratch/atlas-016/spike-client");

    public static readonly string Sandbox = SpikeRoot + "/sandbox.sh";

    // The data path the sandbox is handed. Only ever passed through, never opened.
    public static readonly string ClientData = Environment.GetEnvironmentVariable("ATLAS_SPIKE_CLIENT_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/state/atlas-spike/client/data");

    public static string RunDir(string run) => Path.Combine(SpikeRoot, "runs", run);
}
