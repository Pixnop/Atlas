using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One rule <see cref="WorldOptions.StrictBootDiagnostics"/> ignores a matching
/// <see cref="BootDiagnosticEntry"/> for: declared with
/// <c>[AtlasAllowBootDiagnostic(...)]</c> (<c>Atlas.XUnit</c>), never built by hand. A matched
/// entry still shows up in <see cref="IWorldSession.BootDiagnostics"/>, unaffected: the allowlist
/// only narrows what strict mode fails on, not what Atlas records.</summary>
/// <param name="MessagePattern">A regular expression matched against
/// <see cref="BootDiagnosticEntry.Message"/> (<see cref="System.Text.RegularExpressions.Regex.IsMatch(string)"/>,
/// same bounded match timeout as Atlas's own boot-diagnostics patterns): an entry whose message
/// does not match is never allowed by this rule, regardless of <see cref="Level"/> or
/// <see cref="Source"/>.</param>
/// <param name="Level">When set, the name of an <see cref="EnumLogType"/> member (e.g.
/// <c>"Warning"</c>): an entry only matches this rule at exactly that level. Typed as a string,
/// not <see cref="EnumLogType"/> itself, so <c>Atlas.XUnit</c> (which declares
/// <c>[AtlasAllowBootDiagnostic]</c>) never has to depend on the game assembly; an unrecognized
/// name fails fast at boot rather than silently matching nothing. <see langword="null"/> (the
/// default) matches every level.</param>
/// <param name="Source">When set, an entry only matches this rule when its
/// <see cref="BootDiagnosticEntry.Source"/> equals this value exactly (ordinal); when
/// <see langword="null"/> (the default), every source matches, including <c>"unknown"</c>.</param>
public sealed record AllowedBootDiagnostic(string MessagePattern, string? Level = null, string? Source = null);
