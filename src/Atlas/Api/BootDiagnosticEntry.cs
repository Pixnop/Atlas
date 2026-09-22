using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One engine log entry observed at <see cref="EnumLogType.Warning"/> level or above,
/// while Atlas was recording (see <see cref="IWorldSession.BootDiagnostics"/>).</summary>
/// <param name="Level">The entry's log level: <see cref="EnumLogType.Warning"/>,
/// <see cref="EnumLogType.Error"/> or <see cref="EnumLogType.Fatal"/>. Every other level is
/// never recorded.</param>
/// <param name="Source">Where the entry came from: <c>"engine"</c> for the engine's own central
/// logger (asset loading, recipe resolution, and most engine-internal warnings land here), or a
/// mod's id when the entry was logged through that mod's own <c>Mod.Logger</c> (the engine
/// prefixes those messages with <c>"[modid] "</c> before either logger's watchers see them;
/// <see cref="Message"/> has the prefix already stripped).</param>
/// <param name="Message">The formatted log message (format arguments already substituted).</param>
/// <param name="AssetPath">The asset location the message appears to be about (e.g.
/// <c>"mymod:blocktypes/broken.json"</c> or <c>"mymod:brokenblock"</c>), picked out of
/// <paramref name="Message"/> by the first <c>domain:token</c>-shaped substring it contains, or
/// <see langword="null"/> when it contains none. Best effort: the engine's own messages do not
/// carry a structured asset reference, and a message naming more than one asset (e.g. a recipe
/// error naming both its output and its missing ingredient) only gets the first one.</param>
public sealed record BootDiagnosticEntry(EnumLogType Level, string Source, string Message, string? AssetPath);
