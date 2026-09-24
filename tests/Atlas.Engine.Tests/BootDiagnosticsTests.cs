using Atlas.Api;
using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using Vintagestory.API.Common;

namespace Atlas.Engine.Tests;

/// <summary>Covers boot diagnostics end to end (spec docs/specs/2026-09-23-boot-diagnostics.md),
/// against BootDiagnosticsFixtureMod, a real mod (assets plus one ModSystem) shipping every
/// shape the field feedback on 0.14.0-rc.1 named: three broken assets (a malformed blocktype
/// JSON, a well-formed blocktype JSON with a wrong-typed property, and a grid recipe referencing
/// a missing item - the engine's own asset loader, so these stay Source "unknown"), a warning
/// logged through the shared, unprefixed <c>api.Logger</c> with a hand-written bracket that does
/// not match the mod's own id (stays "unknown", with a hint), a second one whose hand-written
/// bracket IS the mod's own real id (stays "unknown" too - a name match alone is never enough),
/// and a warning logged through the mod's own <c>Mod.Logger</c> (verifies to Source
/// "bootdiagfixture" by channel). These tests pin that Atlas records every one of those by
/// default without failing the boot, attributes honestly, and that <c>StrictBootDiagnostics</c>
/// fails on them unless <c>AllowedBootDiagnostics</c> covers them.</summary>
[Trait("Category", "E2E")]
public class BootDiagnosticsTests
{
    private const string FixtureModPath = "../../../../BootDiagnosticsFixtureMod";

    // A plain class library (no ModSystem, no ModInfoAttribute), copied next to this suite's own
    // output by a build-only ProjectReference (see Atlas.Engine.Tests.csproj); staged directly
    // by file name, like ClientObservationTests' own fixture dll.
    private const string DependencyDll = "DependencyLibraryFixture.dll";

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
                && e.Source == "unknown"
                && e.AssetPath == "bootdiagfixture:blocktypes/malformed.json");
        Assert.Contains(
            entries,
            e => e.Level == EnumLogType.Error
                && e.Source == "unknown"
                && e.AssetPath == "bootdiagfixture:bootdiagbadproperty");
        Assert.Contains(
            entries,
            e => e.Level == EnumLogType.Warning
                && e.Source == "unknown"
                && e.AssetPath == "game:doesnotexistatall");

        // The fixture's own .cs file sits at the root of the staged folder, outside a 'src/'
        // subfolder; without tests/BootDiagnosticsFixtureMod/.ignore, ModContainer.Unpack logs an
        // Error for it before verification is armed, which the name-match fallback then
        // misattributes to "bootdiagfixture" like a real entry.
        Assert.DoesNotContain(
            entries, e => e.Message.Contains("is not in the 'src/' subfolder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BootDiagnostics_Should_VerifySource_When_TheModLogsThroughItsOwnLogger()
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
            e => e.Level == EnumLogType.Warning
                && e.Source == "bootdiagfixture"
                && e.SourceHint == null
                && e.Message.Contains("used its own logger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BootDiagnostics_Should_LeaveSourceUnknown_When_TheModWritesItsOwnBracketThroughApiLogger()
    {
        // The field report's exact shape: a mod's own hand-written logging convention (through
        // the shared api.Logger, no ModLogger involved) reads like a source on the wire but was
        // never verified, and here it does not even match the mod's real id ("BootDiagFixture"
        // vs "bootdiagfixture") - it must stay "unknown", with the parsed text kept as a hint.
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
            e => e.Level == EnumLogType.Warning
                && e.Source == "unknown"
                && e.SourceHint == "BootDiagFixture"
                && e.Message.Contains("boots unconfigured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BootDiagnostics_Should_LeaveSourceUnknown_When_TheHandWrittenBracketIsTheModsOwnRealId()
    {
        // The review case, sharper than the mismatched-bracket test above: even a bracket that
        // exactly matches the mod's real id must not verify when it did not come through that
        // mod's own logger. A name match is never evidence by itself; only the channel is.
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
            e => e.Level == EnumLogType.Warning
                && e.Source == "unknown"
                && e.SourceHint == "bootdiagfixture"
                && e.Message.Contains("not routed through Mod.Logger", StringComparison.Ordinal));
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

        // Pin the render site itself (ServerHost.DescribeStrictFailure -> BootDiagnosticEntry.
        // DescribeSource): the hand-written-bracket fixture entry keeps its hint, the asset
        // errors (no hint at all) render as bare "unknown".
        Assert.Contains("[unknown, hint BootDiagFixture] boots unconfigured", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[unknown] ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_HintADependencyDll_When_APlainLibraryWithNoModSystemIsStagedThroughAtlasMods()
    {
        // The field shape this covers: a consumer lists a dependency dll in [AtlasMods] next to
        // its real mod (meaning "stage this alongside it", not "this is a mod too"). The engine
        // stages every AtlasMods path as its own top-level mod (ModStager.Stage), so a plain
        // library with no ModSystem and no ModInfoAttribute fails ModContainer.LoadModInfo with
        // its own "declared as code mod" message (decompile- and headless-verified from 1.21.7
        // to 1.22.7), unverified (Source "unknown", hinted by the staged file name) because no
        // per-mod-logger channel exists yet at that point in boot.
        await using ServerHost host = TestHosts.New(
            new WorldOptions { StrictBootDiagnostics = true }, DependencyDll);

        AtlasBootDiagnosticsException ex =
            await Assert.ThrowsAsync<AtlasBootDiagnosticsException>(() => host.StartAsync());

        Assert.Contains(
            $"[unknown, hint {DependencyDll}] Exception: ", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            "declared as code mod, but there are no .dll files that contain at least one ModSystem " +
            "or has a ModInfo attribute",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "This looks like a dependency dll staged as a mod of its own: stage it next to the mod " +
            "instead of listing it in AtlasMods.",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("https://github.com/Pixnop/Atlas/wiki/Mod-Staging", ex.Message, StringComparison.Ordinal);

        // The hint is per-entry, not blanket: the OTHER entry the same failure logs ("An
        // exception was thrown trying to to load the ModInfo:", no engine message to recognize)
        // must not get one, or a reader would see the hint twice.
        int hintCount = ex.Message.Split("This looks like a dependency dll").Length - 1;
        Assert.Equal(1, hintCount);
    }

    [Fact]
    public async Task StartAsync_Should_Succeed_When_StrictModeIsOnAndAssetsAreClean()
    {
        await using ServerHost host = new(
            new WorldOptions { StrictBootDiagnostics = true }, Array.Empty<string>(), TestPaths.OwnOutputDirectory);

        Exception? exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_Should_Succeed_When_StrictModeIsOnAndAnAllowlistCoversEveryOffendingEntry()
    {
        var options = new WorldOptions
        {
            StrictBootDiagnostics = true,
            AllowedBootDiagnostics = [new AllowedBootDiagnostic(".*")],
        };
        await using ServerHost host = new(options, new[] { FixtureModPath }, TestPaths.OwnOutputDirectory);

        Exception? exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(exception);

        // The allowlist only narrows what the strict check fails on; BootDiagnostics itself must
        // still show every entry it let through, unfiltered, so a scenario can see what was
        // allowed (today only the pure design, BootDiagnosticsAllowlistTests, pinned this).
        IReadOnlyList<BootDiagnosticEntry> entries = null!;
        await host.RunScenarioAsync(world =>
        {
            entries = world.BootDiagnostics;
            return Task.CompletedTask;
        });

        Assert.Contains(entries, e => e.AssetPath == "bootdiagfixture:blocktypes/malformed.json");
        Assert.Contains(
            entries,
            e => e.Source == "bootdiagfixture" && e.Message.Contains("used its own logger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_Should_StillThrow_When_AllowlistCoversOnlySomeOfTheOffendingEntries()
    {
        // Precision check: an allowlist for the mod's two deliberate warnings must not also swallow
        // the (unrelated, unallowed) broken-asset diagnostics the same boot produces.
        var options = new WorldOptions
        {
            StrictBootDiagnostics = true,
            AllowedBootDiagnostics =
            [
                new AllowedBootDiagnostic("boots unconfigured"),
                new AllowedBootDiagnostic("used its own logger"),
            ],
        };
        await using ServerHost host = new(options, new[] { FixtureModPath }, TestPaths.OwnOutputDirectory);

        AtlasBootDiagnosticsException ex =
            await Assert.ThrowsAsync<AtlasBootDiagnosticsException>(() => host.StartAsync());

        Assert.DoesNotContain("boots unconfigured", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("used its own logger", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bootdiagfixture:blocktypes/malformed.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_ThrowAtlasSetupException_When_AnAllowedDiagnosticLevelIsMisspelled()
    {
        // Filter compiles every rule before it ever reads an entry, so this fails at boot even
        // though nothing here ever logs a Warning-or-above entry to actually filter.
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(MisspelledLevelScenario));
        await using ServerHost host = new(recipe.Options, recipe.ModPaths, recipe.ModBaseDir);

        AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(() => host.StartAsync());

        Assert.Contains("[AtlasAllowBootDiagnostic]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Warnning", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MisspelledLevelScenario), ex.Message, StringComparison.Ordinal);
        Assert.Contains("Warning", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Fatal", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_BootVanilla_When_ClassOptsOutOfAssemblyMods()
    {
        // Atlas.Engine.Tests declares no assembly-level [AtlasMods] and has no MSBuild-generated
        // manifest, so recipe.ModPaths is empty with or without ExcludeAssemblyMods here: this
        // test only proves that a vanilla recipe boots a genuinely clean world, not that
        // ExcludeAssemblyMods itself drops anything (AttributeMapper's own pure tests,
        // AttributeMappingTests.Map_Should_ExcludeAssemblyModsAndManifest_When_ClassOptsOut and
        // Map_Should_KeepOnlyItsOwnMods_When_ClassOptsOutAndDeclaresMods, pin that mapping rule
        // against a fake assembly-level [AtlasMods] instead).
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaOptOutScenario));
        Assert.Empty(recipe.ModPaths);

        await using ServerHost host = new(recipe.Options, recipe.ModPaths, recipe.ModBaseDir);
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
    public async Task StartAsync_Should_StillStageItsOwnMods_When_OptedOutClassDeclaresSome()
    {
        // ExcludeAssemblyMods only drops the assembly-wide set; a class's own Mods still boot.
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaWithOwnFixtureModScenario));
        Assert.Equal(new[] { FixtureModPath }, recipe.ModPaths);

        await using ServerHost host = new(recipe.Options, recipe.ModPaths, recipe.ModBaseDir);
        await host.StartAsync();

        IReadOnlyList<BootDiagnosticEntry> entries = null!;
        await host.RunScenarioAsync(world =>
        {
            entries = world.BootDiagnostics;
            return Task.CompletedTask;
        });

        Assert.Contains(
            entries,
            e => e.Source == "bootdiagfixture" && e.Message.Contains("used its own logger", StringComparison.Ordinal));
    }

    private static ServerHost NewFixtureHost(bool strict = false)
        => new(
            new WorldOptions { StrictBootDiagnostics = strict },
            new[] { FixtureModPath },
            TestPaths.OwnOutputDirectory);

    [AtlasWorld(ExcludeAssemblyMods = true)]
    private sealed class VanillaOptOutScenario
    {
    }

    [AtlasWorld(ExcludeAssemblyMods = true, Mods = new[] { FixtureModPath })]
    private sealed class VanillaWithOwnFixtureModScenario
    {
    }

    [AtlasWorld(StrictBootDiagnostics = true, ExcludeAssemblyMods = true)]
    [AtlasAllowBootDiagnostic(".*", Level = "Warnning")]
    private sealed class MisspelledLevelScenario
    {
    }
}
