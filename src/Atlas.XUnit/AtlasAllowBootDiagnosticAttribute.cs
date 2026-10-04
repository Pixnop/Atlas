namespace Atlas.XUnit;

/// <summary>Declares one boot diagnostic that <c>[AtlasWorld(StrictBootDiagnostics = true)]</c>
/// must not fail on, for a mod with a deliberate <c>Warning</c>-or-above entry (for example a
/// mod that warns on purpose when it boots unconfigured). A matched entry still shows up in
/// <see cref="Atlas.Api.IWorldSession.BootDiagnostics"/>, so a scenario can still see what strict
/// mode let through; only the strict check itself ignores it.</summary>
/// <remarks><para>Stackable (<c>AllowMultiple</c>): declare one attribute per allowed shape.
/// Assembly-level rules apply to every scenario class in the assembly; class-level rules add to
/// them, not replace them.</para>
/// <para>A rule can also ask for the entry it allows: <see cref="Required"/> fails the boot when
/// the rule matches nothing, <see cref="Count"/> when it matches a different number than the one
/// given. That keeps an allowance honest: a warning a mod stops logging, or starts logging twice,
/// is news the rule would otherwise swallow. Like the allowance itself, both are only checked on
/// a class with <c>[AtlasWorld(StrictBootDiagnostics = true)]</c>: without strict mode nothing
/// reads the rules, an unmet one fails nothing and a typo in one is not caught.</para>
/// <para>An assembly-level rule asks for its entry only of a class that loads the assembly's mods.
/// On a class with <c>[AtlasWorld(ExcludeAssemblyMods = true)]</c> the mod that logs the entry is
/// absent, so the rule still allows what it matches there but its <see cref="Required"/> and
/// <see cref="Count"/> are not enforced. A class-level rule is enforced on its class either
/// way.</para></remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class AtlasAllowBootDiagnosticAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see
    /// cref="AtlasAllowBootDiagnosticAttribute"/> class.</summary>
    /// <param name="messagePattern">A regular expression matched against the entry's
    /// <c>Message</c> (bounded match timeout, same as Atlas's own boot-diagnostics patterns): an
    /// unanchored substring match, so <c>"unconfigured"</c> also allows a longer message that
    /// merely contains it; anchor with <c>^</c> and <c>$</c> for a whole-message match. An entry
    /// whose message does not match is never allowed by this rule. There is no literal option: to
    /// allow an exact text that contains regular-expression characters, escape it with
    /// <see cref="System.Text.RegularExpressions.Regex.Escape(string)"/>. An attribute argument is
    /// a constant, so the call cannot sit in the attribute itself: run the text through it once and
    /// paste the result (<c>retrying\ \(1/3\)</c> for <c>retrying (1/3)</c>), or put a backslash
    /// before each metacharacter by hand. The message is
    /// matched after the engine's leading <c>"[name] "</c> prefix has been taken off it: the
    /// prefix is in <see cref="Atlas.Api.BootDiagnosticEntry.SourceHint"/> when the entry's source
    /// is unknown and gone when it is verified, so a pattern never sees it. To narrow a rule by who
    /// logged the entry, use <see cref="Source"/>, not a pattern on the bracket.</param>
    public AtlasAllowBootDiagnosticAttribute(string messagePattern) => MessagePattern = messagePattern;

    /// <summary>Gets the message pattern declared by this attribute.</summary>
    public string MessagePattern { get; }

    /// <summary>Gets or sets the level this rule is restricted to, by exact member name in any
    /// case (e.g. <c>Level = "Warning"</c> or <c>"warning"</c>, matching an <c>EnumLogType</c>
    /// member at <c>Warning</c> or above, the only levels this feature ever records). A string,
    /// not the engine's own enum type, so this package never has to depend on the game assembly,
    /// and engine enum values are compile-time constants that can shift across engine versions
    /// (see ADR 0003); anything other than one of the three accepted member names, spelled exactly
    /// (a typo, a numeric string even one that names a real member, a comma-separated list, or a
    /// level below <c>Warning</c>), throws <see cref="Atlas.Api.AtlasSetupException"/> at boot,
    /// naming this attribute, the rejected value, the class or assembly it sits on, and the three
    /// accepted values, but only when <c>[AtlasWorld(StrictBootDiagnostics = true)]</c> is
    /// actually set on the class: rules are compiled by the strict check itself, so a typo on a
    /// rule attached to a class that never turns strict mode on is never caught. Unset (the
    /// default, <see langword="null"/>) matches every level.</summary>
    public string? Level { get; set; }

    /// <summary>Gets or sets the exact <c>Source</c> this rule is restricted to (a verified mod
    /// id, or the literal <c>"unknown"</c>). Unset (the default) matches every source. A mod that
    /// logs through its own <c>Mod.Logger</c> has a <c>Source</c>, its mod id; one that writes a
    /// bracket by hand through <c>api.Logger</c> has <c>"unknown"</c>. There is no filter on
    /// <c>SourceHint</c>, the clue an unknown entry carries: it is for a reader, not for
    /// matching.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets a value indicating whether the boot fails, under
    /// <c>StrictBootDiagnostics</c>, when this rule matches no entry at all. Off by default.
    /// The failure is the <see cref="Atlas.Api.AtlasBootDiagnosticsException"/> an unallowed entry
    /// throws, and its message names this rule and where it is declared. Without strict mode
    /// nothing is checked.</summary>
    public bool Required { get; set; }

    /// <summary>Gets or sets the exact number of entries this rule must match during the boot:
    /// fewer or more fails it the way <see cref="Required"/> does (a count implies it), and the
    /// entries it did match stay allowed. Counted among the entries the boot logged, per rule: an
    /// entry two rules match counts for both. <c>0</c> (the default) means no constraint on the
    /// number, since an attribute argument cannot be <see langword="null"/>; a negative value
    /// throws <see cref="Atlas.Api.AtlasSetupException"/> at boot, under strict mode, naming this
    /// attribute and where it is declared.</summary>
    public int Count { get; set; }
}
