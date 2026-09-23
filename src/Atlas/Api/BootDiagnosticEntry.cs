using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One engine log entry observed at <see cref="EnumLogType.Warning"/> level or above,
/// while Atlas was recording (see <see cref="IWorldSession.BootDiagnostics"/>).</summary>
/// <param name="Level">The entry's log level: <see cref="EnumLogType.Warning"/>,
/// <see cref="EnumLogType.Error"/> or <see cref="EnumLogType.Fatal"/>. Every other level is
/// never recorded.</param>
/// <param name="Source">The mod that produced the entry, VERIFIED against the mods the engine
/// actually loaded, or the literal <c>"unknown"</c> when no such verification is possible. Two
/// engine-provided routes are verified: an entry logged through a mod's own <c>Mod.Logger</c>
/// (the engine prefixes those with <c>"[modid] "</c>, or <c>"[filename] "</c> when
/// <c>ModInfo</c> failed to parse, before any watcher of the central logger sees them), and an
/// entry the engine itself logs about a specific mod container while loading it (a missing
/// <c>modinfo.json</c>, a failed assembly load: the engine logs those through that same
/// container's <c>Mod.Logger</c> too). Both shapes are cross-checked against every mod id and
/// file name the engine actually loaded (<c>ICoreAPI.ModLoader.Mods</c>), not merely parsed off
/// the message: a mod's own bracketed logging CONVENTION through the shared, unprefixed
/// <c>api.Logger</c> (for example a mod that writes its own <c>"[MyMod] "</c> prefix by hand)
/// looks identical on the wire to a verified entry but is never one, since nothing routes it
/// through that mod's container. <c>"unknown"</c> covers that case, plus the engine's own
/// central-only diagnostics (asset loading, recipe resolution) that never name a mod container
/// at all: Atlas cannot tell those apart from a mod bypassing its own logger, so it says so
/// rather than guessing "engine". See <see cref="SourceHint"/> for what was parsed when
/// <c>Source</c> is <c>"unknown"</c>.</param>
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
    EnumLogType Level, string Source, string Message, string? AssetPath, string? SourceHint = null);
