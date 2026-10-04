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
/// name&gt;</c> under the test project's output directory (the project's build output, without
/// the dll, pdb and xml files of the other tagged mods, which are staged as mods of their own,
/// then its <c>assets/</c> folder and <c>modinfo.json</c> copied over it, so the project's copy
/// wins on a file both hold) and writes that folder. Two folder mods therefore never share a staging name,
/// and the project's <c>assets/</c> need not be copied to its build output. Any other referenced
/// project is a dll mod, and its dll path is written as is. Reach a folder mod one way only: a
/// tagged project that is also listed here by its build output is two copies of one mod under two
/// names, and the engine logs "Multiple mods share the mod ID" and loads only one of them.</para>
/// <para>A <c>modinfo.json</c> that the mod project generates at build counts, as long as it is in
/// the project's build output when the project has finished building: a target after <c>Build</c>
/// that writes it to <c>$(OutDir)</c>, or one that writes it under <c>obj/</c> and copies it with a
/// <c>None</c> item, both work. A template that is itself called <c>modinfo.json</c> and sits next
/// to the project file hides the generated copy, because the project's file is copied over the build
/// output's: keep the template under another name. Atlas generates no <c>modinfo.json</c> itself.</para>
/// <para>At boot Atlas compares each staged code mod's dll (a dll, or the mod's own dll at the
/// root of a folder or zip) with the assembly the engine bound for it, and writes one "[Atlas]
/// staged mod" line per mod to stderr, verified or skipped with the reason (see
/// <see cref="Atlas.Api.AtlasSetupException"/>). The libraries a folder or zip mod ships next to
/// its dll are compared too, each with the assembly of its name the process holds, with one more
/// line each (none for a library the game ships itself). A source mod and a content-only mod
/// have no staged dll to compare and are exempt. The line names each mod by the modid in its
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
