using Atlas.Api;
using Atlas.Engine.Tests.Support;
using Vintagestory.API.Common;

namespace Atlas.Engine.Tests;

/// <summary>Covers boot diagnostics end to end (spec docs/specs/2026-09-23-boot-diagnostics.md),
/// against BootDiagnosticsFixtureMod, a real content-only mod shipping three broken assets: a
/// malformed blocktype JSON, a well-formed blocktype JSON with a wrong-typed property, and a
/// grid recipe referencing a missing item. The engine's own asset loader logs one or two entries
/// per case, five in all (measured shapes are in the spec); these tests pin that Atlas records
/// exactly those entries by default without failing the boot, and that
/// <c>StrictBootDiagnostics</c> fails it instead.</summary>
[Trait("Category", "E2E")]
public class BootDiagnosticsTests
{
    private const string FixtureModPath = "../../../../BootDiagnosticsFixtureMod";

    [Fact]
    public async Task BootDiagnostics_Should_RecordEachFixtureCase_When_ModShipsBrokenAssets()
    {
        await using ServerHost host = NewFixtureHost();
        await host.StartAsync();

        IReadOnlyList<BootDiagnosticEntry> entries = null!;
        await host.RunScenarioAsync(world =>
        {
            entries = world.BootDiagnostics;
            return Task.CompletedTask;
        });

        Assert.Contains(
            entries,
            e => e.Level == EnumLogType.Error
                && e.Source == "engine"
                && e.AssetPath == "bootdiagfixture:blocktypes/malformed.json");
        Assert.Contains(
            entries,
            e => e.Level == EnumLogType.Error
                && e.Source == "engine"
                && e.AssetPath == "bootdiagfixture:bootdiagbadproperty");
        Assert.Contains(
            entries,
            e => e.Level == EnumLogType.Warning
                && e.Source == "engine"
                && e.AssetPath == "game:doesnotexistatall");
    }

    [Fact]
    public async Task BootDiagnostics_Should_StayEmpty_When_NoModShipsBrokenAssets()
    {
        // The regression guard for "default behavior unchanged": a boot with no mod-under-test
        // (the same boot every other suite in this project runs) must not spontaneously report
        // Warning-or-above entries, or every scenario class would inherit a false positive.
        await using ServerHost host = TestHosts.New();
        await host.StartAsync();

        IReadOnlyList<BootDiagnosticEntry> entries = null!;
        await host.RunScenarioAsync(world =>
        {
            entries = world.BootDiagnostics;
            return Task.CompletedTask;
        });

        Assert.Empty(entries);
    }

    [Fact]
    public async Task StartAsync_Should_ThrowAtlasBootDiagnosticsException_When_StrictModeIsOnAndAssetsAreBroken()
    {
        await using ServerHost host = NewFixtureHost(strict: true);

        AtlasBootDiagnosticsException ex =
            await Assert.ThrowsAsync<AtlasBootDiagnosticsException>(() => host.StartAsync());

        Assert.Contains("bootdiagfixture:blocktypes/malformed.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bootdiagfixture:bootdiagbadproperty", ex.Message, StringComparison.Ordinal);
        Assert.Contains("game:doesnotexistatall", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_Succeed_When_StrictModeIsOnAndAssetsAreClean()
    {
        await using ServerHost host = new(
            new WorldOptions { StrictBootDiagnostics = true }, Array.Empty<string>(), TestPaths.OwnOutputDirectory);

        await host.StartAsync();
    }

    private static ServerHost NewFixtureHost(bool strict = false)
        => new(
            new WorldOptions { StrictBootDiagnostics = strict },
            new[] { FixtureModPath },
            TestPaths.OwnOutputDirectory);
}
