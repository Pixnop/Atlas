namespace Atlas.Engine.Tests;

/// <summary>Pins the scratch sweep a <see cref="ServerHost"/> runs at a clean dispose (issue
/// #182): a host built outside the registry, as the engine tests build theirs, does not leave
/// its directory behind, and the same keep reasons the registry honours (the debugging variable,
/// the explicit opt-out) hold for it. The crash and abandoned-thread keeps are pinned where those
/// hosts already exist, in <c>CrashSurfacingTests</c> and <c>TeardownDiagnosticsTests</c>.</summary>
/// <remarks>The keep tests never start the host: the sweep decision does not depend on a
/// running engine, so they stand a file in the directory themselves and cost no boot.</remarks>
[Trait("Category", "E2E")]
public class ServerHostScratchTests
{
    [Fact]
    public async Task DisposeAsync_Should_DeleteTheScratch_When_ABootedHostEndsClean()
    {
        ServerHost host = TestHosts.New();
        await host.StartAsync();
        string dataPath = host.DataPath;
        Assert.True(Directory.Exists(dataPath), "the running host's scratch must exist");

        await host.DisposeAsync();

        Assert.False(Directory.Exists(dataPath), $"the clean host's scratch '{dataPath}' must be deleted");
    }

    [Fact]
    public async Task DisposeAsync_Should_KeepTheScratch_When_SweepOnDisposeIsOff()
    {
        ServerHost host = TestHosts.New();
        host.SweepScratchOnDispose = false;
        string marker = StandInScratch(host);

        await host.DisposeAsync();

        try
        {
            Assert.True(File.Exists(marker), "a host that opted out of the sweep must keep its scratch");
        }
        finally
        {
            Directory.Delete(host.DataPath, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_Should_KeepTheScratch_When_TheKeepVariableIsSet()
    {
        ServerHost host = TestHosts.New();
        string marker = StandInScratch(host);
        string? original = Environment.GetEnvironmentVariable(ScratchRetention.KeepScratchVariable);

        try
        {
            Environment.SetEnvironmentVariable(ScratchRetention.KeepScratchVariable, "1");
            await host.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScratchRetention.KeepScratchVariable, original);
        }

        try
        {
            Assert.True(File.Exists(marker), "ATLAS_KEEP_SCRATCH=1 must keep a direct host's scratch too");
        }
        finally
        {
            Directory.Delete(host.DataPath, recursive: true);
        }
    }

    /// <summary>Creates the host's scratch directory with one file in it, as boot would have.</summary>
    /// <param name="host">The unstarted host.</param>
    /// <returns>The file's path.</returns>
    private static string StandInScratch(ServerHost host)
    {
        Directory.CreateDirectory(host.DataPath);
        string marker = Path.Combine(host.DataPath, "marker.txt");
        File.WriteAllText(marker, "evidence");
        return marker;
    }
}
