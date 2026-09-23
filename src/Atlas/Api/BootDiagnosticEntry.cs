using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One engine log entry observed at <see cref="EnumLogType.Warning"/> level or above,
/// while Atlas was recording (see <see cref="IWorldSession.BootDiagnostics"/>).</summary>
/// <param name="Level">The entry's log level: <see cref="EnumLogType.Warning"/>,
/// <see cref="EnumLogType.Error"/> or <see cref="EnumLogType.Fatal"/>. Every other level is
/// never recorded.</param>
/// <param name="Source">The mod that produced the entry, verified, or the literal
/// <c>"unknown"</c> when no verification is possible. Verified means Atlas actually observed the
/// entry come through that exact mod's own <c>Mod.Logger</c>: Atlas subscribes to every loaded
/// mod's own logger as early as the engine allows (before any mod's own startup code runs), and
/// that logger's <c>EntryAdded</c> firing IS the evidence, not a name parsed off the message. A
/// mod's own bracketed logging convention through the shared, unprefixed <c>api.Logger</c> (for
/// example writing its own <c>"[MyMod] "</c> prefix by hand) looks identical on the wire to a
/// verified entry, even when the bracket happens to be the mod's real id, but is never one:
/// nothing routes it through that mod's own logger, so it stays <c>"unknown"</c>. The one
/// exception is an entry logged before that subscription could exist at all - the engine's own
/// error about a mod container while it is still loading it (a missing <c>modinfo.json</c>, a
/// failed assembly load) - which has no channel to confirm it through; that case falls back to
/// matching its parsed hint against the mods that did end up loading
/// (<c>ICoreAPI.ModLoader.Mods</c>), a real but weaker signal, not misattributable in practice
/// since no mod code has run yet to fake a bracket at that point. <c>"unknown"</c> also covers
/// the engine's own central-only diagnostics (asset loading, recipe resolution) that never name a
/// mod container at all. See <see cref="SourceHint"/> for what was parsed when <c>Source</c> is
/// <c>"unknown"</c>.</param>
/// <param name="Message">The formatted log message (format arguments already substituted), with
/// a leading <c>"[name] "</c>-shaped prefix stripped whenever one is present, regardless of
/// whether it resolved to a verified <see cref="Source"/>.</param>
/// <param name="AssetPath">The asset location the message appears to be about (e.g.
/// <c>"mymod:blocktypes/broken.json"</c> or <c>"mymod:brokenblock"</c>), picked out of
/// <paramref name="Message"/> by the first <c>domain:token</c>-shaped substring it contains, or
/// <see langword="null"/> when it contains none. Best effort: the engine's own messages do not
/// carry a structured asset reference, and a message naming more than one asset (e.g. a recipe
/// error naming both its output and its missing ingredient) only gets the first one.</param>
/// <param name="SourceHint">The <c>"[name] "</c>-shaped prefix stripped off the message, when
/// there was one, whether or not it resolved to a verified <see cref="Source"/>; <see
/// langword="null"/> when the message had no such prefix, and always <see langword="null"/> when
/// <see cref="Source"/> is already verified (the hint added nothing beyond what
/// <see cref="Source"/> already says). Unverified: it can be a mod's own hand-written logging
/// convention, and even a real modid or file name if the mod list could not be consulted yet.
/// Never use it as a trust or filtering signal in place of <see cref="Source"/>; it exists so a
/// human reading <c>"unknown"</c> entries still sees whatever clue the message carried.</param>
public sealed record BootDiagnosticEntry(
    EnumLogType Level, string Source, string Message, string? AssetPath, string? SourceHint = null)
{
    /// <summary>The text to show a human for <see cref="Source"/>: <see cref="Source"/> verbatim,
    /// except when it is still <c>"unknown"</c> and a <see cref="SourceHint"/> was parsed, where it
    /// is <c>"unknown, hint {SourceHint}"</c> so a reader still sees what the message hinted at even
    /// though nothing verified it. A verified <see cref="Source"/> is shown as-is even if
    /// <see cref="SourceHint"/> happens to be set. Every place Atlas renders an entry for humans
    /// (<see cref="AtlasBootDiagnosticsException"/>'s message, any future <c>ToString</c>,
    /// documentation examples) uses this instead of reading <see cref="Source"/> directly, so a
    /// hint is never silently dropped.</summary>
    /// <returns><see cref="Source"/>, with <c>", hint {SourceHint}"</c> appended when
    /// <see cref="Source"/> is <c>"unknown"</c> and a hint was parsed.</returns>
    public string DescribeSource() =>
        Source == "unknown" && SourceHint is { } hint ? $"{Source}, hint {hint}" : Source;
}
