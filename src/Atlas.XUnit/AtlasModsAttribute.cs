namespace Atlas.XUnit;

/// <summary>Declares mod paths, staged for every scenario class in the assembly.</summary>
/// <remarks>As an alternative to listing paths here by hand, mark a <c>ProjectReference</c> to
/// the mod-under-test with <c>&lt;AtlasMod&gt;true&lt;/AtlasMod&gt;</c>. The
/// <c>Pixnop.Atlas.XUnit</c> package ships <c>build/Atlas.E2E.targets</c> as a
/// <c>buildTransitive</c> target, so the import needs no setup; the target writes one path per
/// such reference into <c>atlas-mods.generated.txt</c> next to the test assembly at build time,
/// and Atlas appends those paths after the ones declared here. Paths from both sources are
/// absolute or resolved relative to the test assembly's directory, and are staged the same way
/// either way.
/// <para>A referenced project that has a <c>modinfo.json</c>, in its build output or next to its
/// project file, is a folder mod: the target assembles it as <c>atlas-mods/&lt;assembly
/// name&gt;</c> under the test project's output directory (the project's build output, then its
/// <c>assets/</c> folder and <c>modinfo.json</c> copied over it, so the project's copy wins on a
/// file both hold) and writes that folder. Two folder mods therefore never share a staging name,
/// and the project's <c>assets/</c> need not be copied to its build output. Any other referenced
/// project is a dll mod, and its dll path is written as is.</para>
/// <para>At boot Atlas compares each staged code mod's dll (a dll, or the dlls at the root of a
/// folder or zip) with the assembly the engine bound for it, and writes one "[Atlas] staged mod"
/// line per mod to stderr, verified or skipped with the reason (see
/// <see cref="Atlas.Api.AtlasSetupException"/>). A source mod and a content-only mod have no
/// staged dll to compare and are exempt. The line names each mod by the modid in its
/// <c>modinfo.json</c>, not by its file or folder name, and a plain <c>dotnet test</c> run does not
/// show stderr: pass <c>--logger "console;verbosity=detailed"</c> or read the TRX output.</para></remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AtlasModsAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="AtlasModsAttribute"/> class.</summary>
    /// <param name="paths">Relative or absolute mod paths, resolved against the test assembly's directory.</param>
    public AtlasModsAttribute(params string[] paths) => Paths = paths;

    /// <summary>Gets the mod paths declared for this assembly.</summary>
    public string[] Paths { get; }
}
