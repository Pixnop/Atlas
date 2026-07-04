namespace Atlas.XUnit;

/// <summary>Declares mod paths, staged for every scenario class in the assembly.</summary>
/// <remarks>As an alternative to listing paths here by hand, mark a <c>ProjectReference</c> to
/// the mod-under-test with <c>&lt;AtlasMod&gt;true&lt;/AtlasMod&gt;</c>. Importing
/// <c>build/Atlas.E2E.targets</c> (see <see cref="AtlasModsAttribute"/>'s package,
/// <c>Pixnop.Atlas.XUnit</c>, which ships it as a <c>buildTransitive</c> target) then writes the
/// resolved output path of every such reference into
/// <c>atlas-mods.generated.txt</c> next to the test assembly at build time; <see
/// cref="Internal.AttributeMapper"/> appends those paths after the ones declared here. Paths from
/// both sources are absolute or resolved relative to the test assembly's directory, and are
/// staged the same way either way.</remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AtlasModsAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="AtlasModsAttribute"/> class.</summary>
    /// <param name="paths">Relative or absolute mod paths, resolved against the test assembly's directory.</param>
    public AtlasModsAttribute(params string[] paths) => Paths = paths;

    /// <summary>Gets the mod paths declared for this assembly.</summary>
    public string[] Paths { get; }
}
