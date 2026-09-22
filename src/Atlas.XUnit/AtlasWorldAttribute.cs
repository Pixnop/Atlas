using Atlas.Api;

namespace Atlas.XUnit;

/// <summary>Declares the world configuration a scenario class runs against.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AtlasWorldAttribute : Attribute
{
    /// <summary>Gets or sets the world seed.</summary>
    public int Seed { get; set; } = WorldOptions.DefaultSeed;

    /// <summary>Gets or sets the type of world to create.</summary>
    public string WorldType { get; set; } = WorldOptions.DefaultWorldType;

    /// <summary>Gets or sets the play style for the world.</summary>
    public string PlayStyle { get; set; } = WorldOptions.DefaultPlayStyle;

    /// <summary>Gets or sets extra mod paths for this class, appended after assembly-level mods.</summary>
    public string[] Mods { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the path to a prebuilt world save (<c>.vcdbs</c>) to load instead of
    /// generating a fresh world, absolute or relative to the test assembly's directory. The
    /// fixture's file name does not matter and the fixture is never written to: each test class
    /// runs against its own pristine copy. When set, <see cref="Seed"/>, <see cref="WorldType"/>
    /// and <see cref="PlayStyle"/> are ignored; the savegame carries its own world configuration.</summary>
    public string? SaveFile { get; set; }

    /// <summary>Gets or sets a value indicating whether the class's boot fails when the engine
    /// logged at least one <c>Warning</c>-or-above entry between the start of the boot and the
    /// world becoming ready: a malformed asset, an unresolved recipe ingredient, a mod's own
    /// startup warning. Off by default, so an existing suite's boot behavior is unchanged; those
    /// entries are always recorded and readable through
    /// <see cref="Atlas.Api.IWorldSession.BootDiagnostics"/> regardless of this setting.</summary>
    /// <remarks>The failure is an <see cref="Atlas.Api.AtlasBootDiagnosticsException"/> listing
    /// every offending entry (level, source, message), so a suite that treats "boots clean" as a
    /// contract for its own mod-under-test's assets fails loudly at boot instead of the problem
    /// only ever reaching server-main.log.</remarks>
    public bool StrictBootDiagnostics { get; set; }
}
