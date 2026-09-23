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
    /// entry whose message does not match is never allowed by this rule.</param>
    public AtlasAllowBootDiagnosticAttribute(string messagePattern) => MessagePattern = messagePattern;

    /// <summary>Gets the message pattern declared by this attribute.</summary>
    public string MessagePattern { get; }

    /// <summary>Gets or sets the level this rule is restricted to, by name (e.g.
    /// <c>Level = "Warning"</c>, matching an <c>EnumLogType</c> member). A string, not the
    /// engine's own enum type, so this package never has to depend on the game assembly; an
    /// unrecognized name throws <see cref="Atlas.Api.AtlasSetupException"/> at boot. Unset (the
    /// default, <see langword="null"/>) matches every level.</summary>
    public string? Level { get; set; }

    /// <summary>Gets or sets the exact <c>Source</c> this rule is restricted to (a verified mod
    /// id, or the literal <c>"unknown"</c>). Unset (the default) matches every source.</summary>
    public string? Source { get; set; }
}
