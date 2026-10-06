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
/// <see cref="Level"/> or <see cref="Source"/>. There is no literal option: to allow an exact
/// text that contains regular-expression characters (<c>(</c>, <c>[</c>, <c>.</c>, <c>+</c>),
/// pass it through <see cref="System.Text.RegularExpressions.Regex.Escape(string)"/>. The message
/// is matched as <see cref="BootDiagnosticEntry.Message"/> holds it, that is after the leading
/// <c>"[name] "</c> prefix has moved to <see cref="BootDiagnosticEntry.SourceHint"/> (or been
/// dropped, once the entry's <see cref="Source"/> is verified): a pattern written to match that
/// prefix (<c>^\[MyMod\]</c>) never sees it. To narrow a rule by who logged the entry, use
/// <see cref="Source"/>.</param>
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
/// <see langword="null"/> (the default), every source matches, including <c>"unknown"</c>. A mod
/// that logs through its own <c>Mod.Logger</c> has a verified <c>Source</c>, its mod id; a mod
/// that writes its own bracket through <c>api.Logger</c> does not, so a rule for its warning
/// reads <c>Source = "unknown"</c> or none. There is no filter on
/// <see cref="BootDiagnosticEntry.SourceHint"/>: it is a clue for a reader, not a signal to
/// filter on.</param>
/// <param name="DeclaredOn">The class or assembly the declaring <c>[AtlasAllowBootDiagnostic]</c>
/// attribute sits on (e.g. <c>"class 'MyMod.Scenarios'"</c> or <c>"assembly 'MyMod.Tests'"</c>),
/// named in the exception a bad <see cref="Level"/> throws so a scenario author can find the rule
/// without hunting through every class in the assembly. Empty when a rule is built directly
/// rather than through <c>AttributeMapper</c> (a pure test constructing a rule by hand, say).</param>
public sealed record AllowedBootDiagnostic(
    string MessagePattern, string? Level = null, string? Source = null, string DeclaredOn = "")
{
    /// <summary>Gets a value indicating whether the boot fails when this rule matches no entry at
    /// all. Not a constructor parameter, so the constructor and <c>Deconstruct</c> keep the
    /// four components they always had. Off by default: a rule is an exception, and an exception
    /// that never fires is normally fine.</summary>
    /// <remarks>Only <see cref="WorldOptions.StrictBootDiagnostics"/> reads the rules, so
    /// without it nothing is checked and an unmet rule fails nothing, like the rest of the
    /// allowlist. Under strict mode an unmet rule fails the boot with
    /// <see cref="AtlasBootDiagnosticsException"/>, the same exception an entry no rule allows
    /// throws, and its message names the rule. The rule counts the entries it matches among those
    /// the boot logged (the ones <see cref="BootDiagnosticEntry.Tick"/> leaves null), whether or
    /// not another rule matches them too. An entry logged after the world was ready, by a
    /// background loop of the mod for instance, is not counted, so a rule that is required for a
    /// warning that may come late fails the boot on the runs where it does, instead of ignoring it.
    /// <c>AttributeMapper</c> leaves <see cref="Required"/> and
    /// <see cref="Count"/> unset on an assembly-level rule for a class that excludes the assembly's
    /// mods, since the mod that logs the entry is not loaded there.</remarks>
    public bool Required { get; init; }

    /// <summary>Gets the exact number of entries this rule must match during the boot, or
    /// <see langword="null"/> (the default) for no constraint on the number. Fewer or more fails
    /// the boot exactly as <see cref="Required"/> does, and the entries it did match stay allowed
    /// either way. A count implies <see cref="Required"/>. Must be at least 1, or the strict check
    /// throws <see cref="AtlasSetupException"/>: a rule that has to match nothing allows nothing,
    /// so there is no allow rule to write.</summary>
    /// <remarks>Counted like <see cref="Required"/>: only the entries logged before the world was
    /// ready, the ones <see cref="BootDiagnosticEntry.Tick"/> leaves null, so an entry that comes
    /// later is not in the number.</remarks>
    public int? Count { get; init; }
}
