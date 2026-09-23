using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Engine.Tests;

/// <summary>Pins the review finding on <c>HostRegistry.CreateAsync</c>: a class host whose boot
/// throws (a <c>StrictBootDiagnostics</c> failure here, but the same is true of any other boot
/// crash) must be disposed and joined before the registry hands the next class a host, or the
/// still-tearing-down engine can null process-wide statics under it (issue #8's shutdown hazard,
/// see <c>ServerHost.DisposeAsync</c>'s remarks). Drives <c>HostRegistry</c> directly, the same
/// way <see cref="Atlas.Pure.Tests.XUnit.DeadHostFailFastTests"/> does for the dead-host path,
/// rather than through the nested guinea pig runner: the path under test is entirely inside
/// <c>HostRegistry.CreateAsync</c>, and a real boot failure needs a real embedded server, which
/// is why this lives here instead of in the pure suite.</summary>
[Trait("Category", "E2E")]
public class StrictBootFailureRecoveryTests
{
    private const string FixtureModPath = "../../../../BootDiagnosticsFixtureMod";

    [Fact]
    public async Task GetOrCreateAsync_Should_LeaveTheRegistryCleanForTheNextClass_When_ABootFailsStrict()
    {
        AtlasBootDiagnosticsException failure = await Assert.ThrowsAsync<AtlasBootDiagnosticsException>(
            () => HostRegistry.GetOrCreateAsync(typeof(StrictBrokenBootProbe)));
        Assert.Contains("bootdiagfixture:blocktypes/malformed.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("bootdiagfixture:bootdiagbadproperty", failure.Message, StringComparison.Ordinal);
        Assert.Contains("game:doesnotexistatall", failure.Message, StringComparison.Ordinal);

        // Before the fix, HostRegistry never disposed the failed host: the next GetOrCreateAsync
        // (any class, unrelated to the one that failed) started booting while the failed engine
        // was still tearing down on its own thread, which surfaced here as a raw engine
        // NullReferenceException instead of a clean boot.
        ServerHost clean = await HostRegistry.GetOrCreateAsync(typeof(CleanBootProbe));
        await clean.RunScenarioAsync(world =>
        {
            Assert.Empty(world.BootDiagnostics);
            return Task.CompletedTask;
        });
    }

    [AtlasWorld(StrictBootDiagnostics = true, Mods = new[] { FixtureModPath })]
    private sealed class StrictBrokenBootProbe
    {
    }

    [AtlasWorld(Seed = 4242)]
    private sealed class CleanBootProbe
    {
    }
}
