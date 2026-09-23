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

    /// <summary>Gets or sets extra mod paths for this class, appended after assembly-level mods
    /// (unless <see cref="ExcludeAssemblyMods"/> is set, in which case these are the only mods
    /// staged).</summary>
    public string[] Mods { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets a value indicating whether this class's host boots WITHOUT the
    /// assembly-wide mod set: neither the assembly-level <c>[AtlasMods(...)]</c> paths nor the
    /// MSBuild-generated manifest (<c>&lt;AtlasMod&gt;true&lt;/AtlasMod&gt;</c> project
    /// references) are staged, only whatever this class's own <see cref="Mods"/> lists. Off by
    /// default, so an existing suite's mod set is unchanged. Lets one class boot a vanilla
    /// baseline (no mods, or a deliberately narrower set) alongside classes that stage the
    /// assembly's usual mods-under-test, e.g. to classify a boot diagnostic as coming from the
    /// mod or from a clean engine (a real field need: a mod author booting a standalone class
    /// with no mods to tell the two apart). Each class always gets its own freshly-booted host
    /// (<c>HostRegistry</c> disposes and recreates whenever the owning class changes), so this
    /// never risks a class with a different mod set reusing another class's host.</summary>
    public bool ExcludeAssemblyMods { get; set; }

    /// <summary>Gets or sets the path to a prebuilt world save (<c>.vcdbs</c>) to load instead of
    /// generating a fresh world, absolute or relative to the test assembly's directory. The
    /// fixture's file name does not matter and the fixture is never written to: each test class
    /// runs against its own pristine copy. When set, <see cref="Seed"/>, <see cref="WorldType"/>
    /// and <see cref="PlayStyle"/> are ignored; the savegame carries its own world configuration.</summary>
    public string? SaveFile { get; set; }

    /// <summary>Gets or sets a value indicating whether the class's boot fails when the engine
    /// logged at least one <c>Warning</c>-or-above entry between the start of the boot and the
    /// world becoming ready: a malformed asset, an unresolved recipe ingredient, a mod's own
    /// startup warning (the engine's tick-overload warning excepted, see
    /// <see cref="Atlas.Api.IWorldSession.BootDiagnostics"/>). Off by default, so an existing
    /// suite's boot behavior is unchanged; those entries are always recorded and readable through
    /// <see cref="Atlas.Api.IWorldSession.BootDiagnostics"/> regardless of this setting.</summary>
    /// <remarks>The failure is an <see cref="Atlas.Api.AtlasBootDiagnosticsException"/> listing
    /// every offending entry (level, source, message), so a suite that treats "boots clean" as a
    /// contract for its own mod-under-test's assets fails loudly at boot instead of the problem
    /// only ever reaching server-main.log.</remarks>
    public bool StrictBootDiagnostics { get; set; }
}
