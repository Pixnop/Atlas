using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace BootDiagnosticsFixtureMod;

/// <summary>The two code-routed shapes <c>docs/specs/2026-09-23-boot-diagnostics.md</c>'s field
/// feedback names, alongside the three broken assets modinfo.json's sibling <c>assets/</c>
/// folder already ships: a warning logged through the shared, unprefixed <c>api.Logger</c> with
/// a hand-written bracket (mirrors a real mod's own logging convention - Nimbus writes
/// <c>"[Nimbus] "</c> itself, not its modid <c>"nimbusserver"</c>), and a warning logged through
/// this mod's own <c>Mod.Logger</c>, which the engine prefixes and verifiably attributes to this
/// exact mod container. Neither is fatal and both fire unconditionally at boot, so every scenario
/// that stages this fixture sees the same five asset entries as before plus these two.</summary>
public sealed class BootDiagnosticsFixtureModSystem : ModSystem
{
    /// <summary>The hand-written bracket in <see cref="ManualPrefixWarning"/>: reads like a
    /// source but was never verified by the engine, and does not even match this mod's own id
    /// ("bootdiagfixture") - the shape <c>BootDiagnosticEntry.Source</c> must never trust.</summary>
    public const string ManualPrefixHint = "BootDiagFixture";

    /// <summary>Logged through <c>api.Logger</c> (the shared central logger), with a bracket this
    /// mod writes by hand: on the wire this is indistinguishable from a genuine engine message
    /// that happens to start the same way, so it must stay <c>Source == "unknown"</c> with
    /// <c>SourceHint == "BootDiagFixture"</c>, never a verified source.</summary>
    public const string ManualPrefixWarning = $"[{ManualPrefixHint}] boots unconfigured, using defaults";

    /// <summary>Logged through <c>Mod.Logger</c> (this container's own logger): the engine
    /// stamps the real mod id on it before any watcher of the central logger sees it, so this one
    /// verifies to <c>Source == "bootdiagfixture"</c>.</summary>
    public const string OwnLoggerWarning = "used its own logger to report a non-fatal setup issue";

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Warning(ManualPrefixWarning);
        Mod.Logger.Warning(OwnLoggerWarning);
    }
}
