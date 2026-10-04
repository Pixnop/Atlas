using Atlas.Api;
using Atlas.Internal.Hosting;
using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

/// <summary>What the registry does with the host a class released at its end (issue #182, ADR
/// 0011): the scratch stays for the harvest seam, the next boot or the process exit sweeps it
/// under the same keep rules as any other disposed host, and each released host is swept once.
/// The hosts are never started: a released host is already disposed when it is remembered, so an
/// unstarted host with a stand-in save in its scratch path is the same thing to the registry. The
/// collection serializes the class with the other tests that touch the registry's process-wide
/// state.</summary>
[Collection("HostRegistry")]
public class ReleasedHostTests
{
    [Fact]
    public void DisposeCurrentBestEffort_Should_SweepTheReleasedScratchOnce_When_ItsClassHasNoFailure()
    {
        ServerHost host = NewHost();
        StandInSave(host);
        HostRegistry.RememberReleased(host, typeof(GreenProbeScenarios));
        Assert.Same(host, HostRegistry.ReleasedHost);

        HostRegistry.DisposeCurrentBestEffort();

        Assert.False(Directory.Exists(host.DataPath), "the released host's scratch must be swept at process exit");
        Assert.Null(HostRegistry.ReleasedHost);

        // The host is forgotten once swept: a second disposal must not touch the path again.
        StandInSave(host);
        HostRegistry.DisposeCurrentBestEffort();
        AssertKeptThenDelete(host, "a swept host must not be swept twice");
    }

    [Fact]
    public void DisposeCurrentBestEffort_Should_KeepTheReleasedScratch_When_ItsClassFailed()
    {
        ScratchLedger.RecordFailure(typeof(RedProbeScenarios));
        ServerHost host = NewHost();
        StandInSave(host);
        HostRegistry.RememberReleased(host, typeof(RedProbeScenarios));

        HostRegistry.DisposeCurrentBestEffort();

        AssertKeptThenDelete(host, "a red class's released scratch is post-mortem evidence and must be kept");
        Assert.Null(HostRegistry.ReleasedHost);
    }

    [Fact]
    public void DisposeCurrentBestEffort_Should_KeepTheReleasedScratch_When_TheOwnerIsUnknown()
    {
        ServerHost host = NewHost();
        StandInSave(host);
        HostRegistry.RememberReleased(host, owner: null);

        HostRegistry.DisposeCurrentBestEffort();

        AssertKeptThenDelete(host, "an unknown owner must read as a failure and keep the scratch");
    }

    [Fact]
    public void RememberReleased_Should_SweepThePreviousHost_When_AnotherOneIsReleased()
    {
        ServerHost first = NewHost();
        StandInSave(first);
        ServerHost second = NewHost();
        StandInSave(second);
        HostRegistry.RememberReleased(first, typeof(GreenProbeScenarios));

        HostRegistry.RememberReleased(second, typeof(GreenProbeScenarios));

        Assert.False(Directory.Exists(first.DataPath), "at most one released host is held: the older one is swept");
        Assert.Same(second, HostRegistry.ReleasedHost);

        HostRegistry.DisposeCurrentBestEffort();
        Assert.False(Directory.Exists(second.DataPath));
    }

    [Fact]
    public async Task ShutDownAndHarvestSavePathAsync_Should_ReturnTheReleasedHostsSaveAndKeepItsScratch_When_NoHostIsLive()
    {
        // What a CLI built before the class-end release depends on: the builder's class has
        // already released its host when `atlas fixture` calls the seam, and the save the
        // graceful release persisted must still be there to copy.
        ServerHost host = NewHost();
        StandInSave(host);
        HostRegistry.RememberReleased(host, typeof(GreenProbeScenarios));

        string? savePath = await HostRegistry.ShutDownAndHarvestSavePathAsync();

        Assert.Equal(host.SaveFilePath, savePath);
        Assert.True(File.Exists(savePath), "the harvest must leave the persisted save in place for the caller");
        Assert.Same(host, HostRegistry.ReleasedHost);

        // Harvesting twice is harmless, and the sweep still waits for the process exit.
        Assert.Equal(host.SaveFilePath, await HostRegistry.ShutDownAndHarvestSavePathAsync());
        HostRegistry.DisposeCurrentBestEffort();
        Assert.False(Directory.Exists(host.DataPath));
    }

    [Fact]
    public async Task ReleaseAtClassEndAsync_Should_DoNothing_When_NoHostIsLive()
    {
        ServerHost remembered = NewHost();
        StandInSave(remembered);
        HostRegistry.RememberReleased(remembered, typeof(GreenProbeScenarios));

        Exception? failure = await Record.ExceptionAsync(HostRegistry.ReleaseAtClassEndAsync);

        Assert.Null(failure);
        Assert.Same(remembered, HostRegistry.ReleasedHost);
        HostRegistry.DisposeCurrentBestEffort();
    }

    [Fact]
    public async Task ReleaseAtClassEndAsync_Should_LeaveTheGateToItsHolder_When_AnotherRequestIsInFlight()
    {
        HostRegistry.EnterExclusive();
        try
        {
            Exception? failure = await Record.ExceptionAsync(HostRegistry.ReleaseAtClassEndAsync);

            // Not ours to release, and not ours to report: the request holding the gate is.
            Assert.Null(failure);
            Assert.Throws<AtlasSetupException>(HostRegistry.EnterExclusive);
        }
        finally
        {
            HostRegistry.ExitExclusive();
        }
    }

    [Fact]
    public async Task ReleaseAtClassEndAsync_Should_ReleaseTheGate_When_ItCompletes()
    {
        await HostRegistry.ReleaseAtClassEndAsync();

        Exception? reentry = Record.Exception(HostRegistry.EnterExclusive);
        HostRegistry.ExitExclusive();

        Assert.Null(reentry);
    }

    [Fact]
    public async Task ClassLifetime_Should_ForwardTheClassEndToTheRegistry_When_XunitDisposesIt()
    {
        // The fixture xUnit builds for every scenario class. With no live host this releases
        // nothing, which is the point: it must complete and leave the registry usable.
        IAsyncLifetime lifetime = new AtlasClassLifetime();
        await lifetime.InitializeAsync();

        await lifetime.DisposeAsync();

        Exception? reentry = Record.Exception(HostRegistry.EnterExclusive);
        HostRegistry.ExitExclusive();
        Assert.Null(reentry);
    }

    [Fact]
    public void AtlasScenarioBase_Should_DeclareTheClassLifetimeFixture_When_AClassDerivesFromIt()
    {
        Assert.True(typeof(IClassFixture<AtlasClassLifetime>).IsAssignableFrom(typeof(DerivedProbeScenarios)));
        Assert.True(typeof(IAsyncLifetime).IsAssignableFrom(typeof(AtlasClassLifetime)));
    }

    private static ServerHost NewHost() => new(new WorldOptions(), [], AppContext.BaseDirectory);

    private static void StandInSave(ServerHost host)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(host.SaveFilePath)!);
        File.WriteAllText(host.SaveFilePath, "persisted world");
    }

    private static void AssertKeptThenDelete(ServerHost host, string message)
    {
        try
        {
            Assert.True(File.Exists(host.SaveFilePath), message);
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

    /// <summary>A scenario class as a consumer writes it: no constructor, no fixture of its own.</summary>
    private sealed class DerivedProbeScenarios : AtlasScenarioBase
    {
    }
}
