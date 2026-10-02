namespace Atlas.Engine.Tests;

/// <summary>Pins that a <see cref="ServerHost"/> built outside the registry, as the engine tests
/// build theirs, deletes the scratch directory of a booted server at a clean dispose (issue #182):
/// the world save, the logs and the staged mods the engine wrote, not a stand-in file. The sweep
/// decision and its keep reasons are in the pure suite (<c>ServerHostScratchSweepTests</c>), the
/// crash and abandoned-thread keeps in <c>CrashSurfacingTests</c> and
/// <c>TeardownDiagnosticsTests</c>, where those hosts already exist.</summary>
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
}
