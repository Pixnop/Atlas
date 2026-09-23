using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One rule <see cref="WorldOptions.StrictBootDiagnostics"/> ignores a matching
/// <see cref="BootDiagnosticEntry"/> for: declared with
/// <c>[AtlasAllowBootDiagnostic(...)]</c> (<c>Atlas.XUnit</c>), never built by hand. A matched
/// entry still shows up in <see cref="IWorldSession.BootDiagnostics"/>, unaffected: the allowlist
/// only narrows what strict mode fails on, not what Atlas records.</summary>
/// <param name="MessagePattern">A regular expression matched against
/// <see cref="BootDiagnosticEntry.Message"/> (<see cref="System.Text.RegularExpressions.Regex.IsMatch(string)"/>,
/// an unanchored substring match, same bounded match timeout as Atlas's own boot-diagnostics
/// patterns): an entry whose message does not match is never allowed by this rule, regardless of
/// <see cref="Level"/> or <see cref="Source"/>.</param>
/// <param name="Level">When set, the exact name of an <see cref="EnumLogType"/> member at
/// <see cref="EnumLogType.Warning"/> or above, in any case (e.g. <c>"Warning"</c> or
/// <c>"warning"</c>, the only levels this feature ever records): an entry only matches this rule
/// at exactly that level. Typed as a string, not <see cref="EnumLogType"/> itself, so
/// <c>Atlas.XUnit</c> (which declares <c>[AtlasAllowBootDiagnostic]</c>) never has to depend on
/// the game assembly, and engine enum values are compile-time constants that can shift across
/// engine versions (see ADR 0003). Anything other than one of the three accepted member names,
/// spelled exactly (a typo, a numeric string even one that names a real member, a comma-separated
/// list, or a level below <c>Warning</c>), fails fast at boot rather than silently matching
/// nothing. <see langword="null"/> (the default) matches every level.</param>
/// <param name="Source">When set, an entry only matches this rule when its
/// <see cref="BootDiagnosticEntry.Source"/> equals this value exactly (ordinal); when
/// <see langword="null"/> (the default), every source matches, including <c>"unknown"</c>.</param>
/// <param name="DeclaredOn">The class or assembly the declaring <c>[AtlasAllowBootDiagnostic]</c>
/// attribute sits on (e.g. <c>"class 'MyMod.Scenarios'"</c> or <c>"assembly 'MyMod.Tests'"</c>),
/// named in the exception a bad <see cref="Level"/> throws so a scenario author can find the rule
/// without hunting through every class in the assembly. Empty when a rule is built directly
/// rather than through <c>AttributeMapper</c> (a pure test constructing a rule by hand, say).</param>
public sealed record AllowedBootDiagnostic(
    string MessagePattern, string? Level = null, string? Source = null, string DeclaredOn = "");
