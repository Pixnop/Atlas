using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The scratch sweep <see cref="ServerHost.DisposeAsync"/> runs for a host built outside
/// the registry (issue #182): a clean dispose deletes the directory, and the opt-out and the
/// debugging variable keep it. None of these start the host. The sweep decision does not depend
/// on a running engine, so each test stands a file in the host's scratch path as boot would have,
/// and the booted case, with the engine's own files in the directory, is in
/// <c>Atlas.Engine.Tests</c>. The collection serializes the class with the other tests that read
/// the process-wide environment through the registry.</summary>
[Collection("HostRegistry")]
public class ServerHostScratchSweepTests
{
    [Fact]
    public async Task DisposeAsync_Should_DeleteTheScratch_When_TheHostEndsClean()
    {
        ServerHost host = NewHost();
        string marker = StandInScratch(host);

        await host.DisposeAsync();

        Assert.False(Directory.Exists(host.DataPath), $"the clean host's scratch '{host.DataPath}' must be deleted");
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task DisposeAsync_Should_KeepTheScratch_When_SweepOnDisposeIsOff()
    {
        ServerHost host = NewHost();
        host.SweepScratchOnDispose = false;
        string marker = StandInScratch(host);

        await host.DisposeAsync();

        AssertKeptThenDelete(host, marker, "a host that opted out of the sweep must keep its scratch");
    }

    [Fact]
    public async Task DisposeAsync_Should_KeepTheScratch_When_TheKeepVariableIsSet()
    {
        ServerHost host = NewHost();
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

        AssertKeptThenDelete(host, marker, "ATLAS_KEEP_SCRATCH=1 must keep a direct host's scratch too");
    }

    [Fact]
    public void SweepScratchOnDispose_Should_BeOn_When_TheHostIsBuiltDirectly()
    {
        // The default is what makes a host built outside the registry clean up after itself.
        Assert.True(NewHost().SweepScratchOnDispose);
    }

    private static ServerHost NewHost() => new(new WorldOptions(), [], AppContext.BaseDirectory);

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

    private static void AssertKeptThenDelete(ServerHost host, string marker, string message)
    {
        try
        {
            Assert.True(File.Exists(marker), message);
        }
        finally
        {
            Directory.Delete(host.DataPath, recursive: true);
        }
    }
}
