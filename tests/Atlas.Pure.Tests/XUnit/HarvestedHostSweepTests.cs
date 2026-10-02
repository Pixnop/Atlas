using Atlas.Internal.Hosting;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

/// <summary>What the registry's process-exit disposal does with the hosts the harvest seam
/// disposed earlier (issue #182): their scratch directories are swept under the same keep rules as
/// any other disposed host, a red class or an unknown owner keeping its directory, and each host
/// is swept once. The hosts are never started: the harvested host of <c>atlas fixture</c> is
/// already disposed by the time it is remembered, so an unstarted host with a stand-in file in its
/// scratch path is the same thing to the sweep. The collection serializes the class with the
/// other tests that touch the registry's process-wide state.</summary>
[Collection("HostRegistry")]
public class HarvestedHostSweepTests
{
    [Fact]
    public void DisposeCurrentBestEffort_Should_SweepTheHarvestedScratchOnce_When_ItsClassHasNoFailure()
    {
        ServerHost host = NewHost();
        StandInScratch(host);
        HostRegistry.RememberHarvested(host, typeof(GreenProbeScenarios));

        HostRegistry.DisposeCurrentBestEffort();

        Assert.False(Directory.Exists(host.DataPath), "the harvested host's scratch must be swept at process exit");

        // The host is forgotten once swept: a second disposal must not touch the path again.
        StandInScratch(host);
        HostRegistry.DisposeCurrentBestEffort();
        AssertKeptThenDelete(host, "a swept host must not be swept twice");
    }

    [Fact]
    public void DisposeCurrentBestEffort_Should_KeepTheHarvestedScratch_When_ItsClassFailed()
    {
        ScratchLedger.RecordFailure(typeof(RedProbeScenarios));
        ServerHost host = NewHost();
        StandInScratch(host);
        HostRegistry.RememberHarvested(host, typeof(RedProbeScenarios));

        HostRegistry.DisposeCurrentBestEffort();

        AssertKeptThenDelete(host, "a red class's harvested scratch is post-mortem evidence and must be kept");
    }

    [Fact]
    public void DisposeCurrentBestEffort_Should_KeepTheHarvestedScratch_When_TheOwnerIsUnknown()
    {
        ServerHost host = NewHost();
        StandInScratch(host);
        HostRegistry.RememberHarvested(host, owner: null);

        HostRegistry.DisposeCurrentBestEffort();

        AssertKeptThenDelete(host, "an unknown owner must read as a failure and keep the scratch");
    }

    private static ServerHost NewHost() => new(new WorldOptions(), [], AppContext.BaseDirectory);

    private static void StandInScratch(ServerHost host)
    {
        Directory.CreateDirectory(host.DataPath);
        File.WriteAllText(Path.Combine(host.DataPath, "marker.txt"), "evidence");
    }

    private static void AssertKeptThenDelete(ServerHost host, string message)
    {
        try
        {
            Assert.True(File.Exists(Path.Combine(host.DataPath, "marker.txt")), message);
        }
        finally
        {
            Directory.Delete(host.DataPath, recursive: true);
        }
    }

    /// <summary>Owner whose class never failed.</summary>
    private sealed class GreenProbeScenarios
    {
    }

    /// <summary>Owner whose class failed, recorded in the process-wide ledger.</summary>
    private sealed class RedProbeScenarios
    {
    }
}
