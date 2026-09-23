namespace Atlas.XUnit;

/// <summary>Declares one boot diagnostic that <c>[AtlasWorld(StrictBootDiagnostics = true)]</c>
/// must not fail on, for a mod with a deliberate <c>Warning</c>-or-above entry (for example a
/// mod that warns on purpose when it boots unconfigured). A matched entry still shows up in
/// <see cref="Atlas.Api.IWorldSession.BootDiagnostics"/>, so a scenario can still see what strict
/// mode let through; only the strict check itself ignores it.</summary>
/// <remarks>Stackable (<c>AllowMultiple</c>): declare one attribute per allowed shape. Assembly-
/// level rules apply to every scenario class in the assembly; class-level rules add to them, not
/// replace them.</remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class AtlasAllowBootDiagnosticAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see
    /// cref="AtlasAllowBootDiagnosticAttribute"/> class.</summary>
    /// <param name="messagePattern">A regular expression matched against the entry's
    /// <c>Message</c> (bounded match timeout, same as Atlas's own boot-diagnostics patterns): an
    /// unanchored substring match, so <c>"unconfigured"</c> also allows a longer message that
    /// merely contains it; anchor with <c>^</c> and <c>$</c> for a whole-message match. An entry
    /// whose message does not match is never allowed by this rule.</param>
    public AtlasAllowBootDiagnosticAttribute(string messagePattern) => MessagePattern = messagePattern;

    /// <summary>Gets the message pattern declared by this attribute.</summary>
    public string MessagePattern { get; }

    /// <summary>Gets or sets the level this rule is restricted to, by name (e.g.
    /// <c>Level = "Warning"</c>, matching an <c>EnumLogType</c> member at <c>Warning</c> or
    /// above, the only levels this feature ever records). A string, not the engine's own enum
    /// type, so this package never has to depend on the game assembly; an unrecognized name, or
    /// one below <c>Warning</c>, throws <see cref="Atlas.Api.AtlasSetupException"/> at boot, but
    /// only when <c>[AtlasWorld(StrictBootDiagnostics = true)]</c> is actually set on the class:
    /// rules are compiled by the strict check itself, so a typo on a rule attached to a class that
    /// never turns strict mode on is never caught. Unset (the default, <see langword="null"/>)
    /// matches every level.</summary>
    public string? Level { get; set; }

    /// <summary>Gets or sets the exact <c>Source</c> this rule is restricted to (a verified mod
    /// id, or the literal <c>"unknown"</c>). Unset (the default) matches every source.</summary>
    public string? Source { get; set; }
}
