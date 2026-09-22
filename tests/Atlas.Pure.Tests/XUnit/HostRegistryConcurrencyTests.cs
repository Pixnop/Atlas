namespace Atlas.Pure.Tests.XUnit;

using Atlas.Api;
using Atlas.XUnit.Internal;

/// <summary>Exercises <see cref="HostRegistry"/>'s concurrent-request guard without booting a real
/// embedded server. <c>GetOrCreateAsync</c>/<c>RecycleAsync</c> both delegate their mutual-exclusion
/// check to the internal <c>EnterExclusive</c>/<c>ExitExclusive</c> pair before ever touching
/// <c>ServerHost</c>; calling that pair directly exercises the exact guard those methods run,
/// without needing two live server boots. That alone does not prove the entry points make their
/// own guard call, though, so <c>GetOrCreateAsync</c> and <c>ShutDownAndHarvestSavePathAsync</c>
/// get their own tests below too, pre-claiming the gate (or marking a probe class dead) to check
/// each one's own call and its finally-block release. The collection serializes this class with
/// every other test touching the registry's process-wide gate (e.g. the fixture-harvest tests),
/// which would otherwise observe the gate held busy here and fail spuriously.</summary>
[Collection("HostRegistry")]
public class HostRegistryConcurrencyTests
{
    [Fact]
    public void EnterExclusive_Should_ThrowAtlasSetupException_When_AlreadyBusy()
    {
        HostRegistry.EnterExclusive();
        try
        {
            AtlasSetupException ex = Assert.Throws<AtlasSetupException>(HostRegistry.EnterExclusive);

            // The message has to name the fix, because the symptom (a second scenario class
            // starting mid-boot) says nothing about parallelization.
            Assert.Contains("DisableTestParallelization", ex.Message);
        }
        finally
        {
            HostRegistry.ExitExclusive();
        }
    }

    [Fact]
    public void ExitExclusive_Should_ReleaseTheGate_When_TheRequestIsDone()
    {
        HostRegistry.EnterExclusive();
        HostRegistry.ExitExclusive();

        // EnterExclusive only returns instead of throwing (see the test above) when the gate is
        // free, so a clean Record.Exception here is the assertion that ExitExclusive released it.
        Exception? reentry = Record.Exception(HostRegistry.EnterExclusive);
        HostRegistry.ExitExclusive();

        Assert.Null(reentry);
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_ThrowArgumentNullException_When_TestClassIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => HostRegistry.GetOrCreateAsync(null!));
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_ThrowAtlasSetupException_When_AlreadyBusy()
    {
        // Marked dead too, so that if the guard were ever skipped this would still fail fast on
        // ThrowIfDead instead of falling through into a real server boot: the assertion below
        // tells the two apart by exception type, and neither path touches ServerHost.
        HostRegistry.MarkDead(typeof(BusyProbeScenarios), "would only surface if the busy guard were skipped");
        HostRegistry.EnterExclusive();
        try
        {
            AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(
                () => HostRegistry.GetOrCreateAsync(typeof(BusyProbeScenarios)));
            Assert.Contains("DisableTestParallelization", ex.Message);
        }
        finally
        {
            HostRegistry.ExitExclusive();
        }
    }

    [Fact]
    public async Task ShutDownAndHarvestSavePathAsync_Should_ThrowAtlasSetupException_When_AlreadyBusy()
    {
        HostRegistry.EnterExclusive();
        try
        {
            await Assert.ThrowsAsync<AtlasSetupException>(HostRegistry.ShutDownAndHarvestSavePathAsync);
        }
        finally
        {
            HostRegistry.ExitExclusive();
        }
    }

    [Fact]
    public async Task ShutDownAndHarvestSavePathAsync_Should_ReleaseTheGate_When_ItCompletes()
    {
        // No host is live in the pure suite, so this returns null rather than harvesting
        // anything; pin that documented return too, not just that its finally released the gate.
        Assert.Null(await HostRegistry.ShutDownAndHarvestSavePathAsync());

        Exception? reentry = Record.Exception(HostRegistry.EnterExclusive);
        HostRegistry.ExitExclusive();

        Assert.Null(reentry);
    }

    /// <summary>The class <see cref="GetOrCreateAsync_Should_ThrowAtlasSetupException_When_AlreadyBusy"/>
    /// marks dead; never booted, so marking it dead costs nothing.</summary>
    private sealed class BusyProbeScenarios
    {
    }
}
