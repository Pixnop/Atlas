using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace BootDiagnosticsFixtureMod;

/// <summary>The code-routed shapes <c>docs/specs/2026-09-23-boot-diagnostics.md</c>'s field
/// feedback names, alongside the three broken assets modinfo.json's sibling <c>assets/</c>
/// folder already ships: a warning logged through the shared, unprefixed <c>api.Logger</c> with
/// a hand-written bracket that does not even match this mod's own id (mirrors a real mod's own
/// logging convention - a consumer's server mod writes its own name in brackets itself, not its
/// real mod id), a second one through <c>api.Logger</c> whose hand-written bracket IS
/// this mod's own real id (the review case: a name match alone must never be enough), and a
/// warning logged through this mod's own <c>Mod.Logger</c>, which Atlas observes directly through
/// that exact logger's own channel and so verifiably attributes to this mod container. None is
/// fatal and all three fire unconditionally at boot, so every scenario that stages this fixture
/// sees the same five asset entries as before plus these three.</summary>
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

    /// <summary>This mod's own real id (matches <c>modinfo.json</c>), used as a hand-written
    /// bracket below rather than a fallback name. Lower case, unlike <see cref="ManualPrefixHint"/>,
    /// because a real mod id is.</summary>
    public const string RealModId = "bootdiagfixture";

    /// <summary>Logged through <c>api.Logger</c> with a hand-written bracket that IS this mod's
    /// own real id, not merely a similar-looking one: on the wire this is still indistinguishable
    /// from a genuine <c>Mod.Logger</c> echo, so it must ALSO stay <c>Source == "unknown"</c> with
    /// <c>SourceHint == "bootdiagfixture"</c>. Nothing about the bracket text ever verifies a
    /// source; only actually going through this mod's own logger does (see
    /// <see cref="OwnLoggerWarning"/>).</summary>
    public const string ExactIdManualWarning = $"[{RealModId}] hand-written, not routed through Mod.Logger";

    /// <summary>Logged through <c>Mod.Logger</c> (this container's own logger): Atlas subscribes
    /// to it directly (see <c>ServerHost.SubscribeModLoggers</c>) before this method ever runs, so
    /// this one verifies to <c>Source == "bootdiagfixture"</c> by channel, not by name.</summary>
    public const string OwnLoggerWarning = "used its own logger to report a non-fatal setup issue";

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Warning(ManualPrefixWarning);
        api.Logger.Warning(ExactIdManualWarning);
        Mod.Logger.Warning(OwnLoggerWarning);
    }
}
