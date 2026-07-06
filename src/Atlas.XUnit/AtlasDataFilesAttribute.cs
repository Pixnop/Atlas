namespace Atlas.XUnit;

/// <summary>Declares files to seed into the embedded server's scratch data path before it boots,
/// so files a mod reads during startup — most commonly <c>api.LoadModConfig("….json")</c> from
/// <c>ModConfig/</c> in <c>StartServerSide</c> — are already in place.</summary>
/// <remarks>
/// <para>Each source path (file or directory, resolved like mod paths: absolute or relative to
/// the test assembly's directory) is copied into the data path before <c>ServerMain</c> launches.
/// A directory's <em>contents</em> are copied, not the directory itself, into
/// <see cref="TargetPath"/>; a file lands inside <see cref="TargetPath"/> under its own name.</para>
/// <para>Two equivalent styles:</para>
/// <code>
/// // Point a fixture folder at a specific data-path subfolder:
/// [AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]
///
/// // Or lay the fixture tree out like the data path itself and overlay it onto the root:
/// [AtlasDataFiles("fixtures/serverdata")]   // contains ModConfig/mymod.json, Macros/…, etc.
/// </code>
/// <para>Assembly-level attributes apply to every scenario class; class-level attributes are
/// copied after them, so on a file name collision the class-level seed wins.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class AtlasDataFilesAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="AtlasDataFilesAttribute"/> class.</summary>
    /// <param name="sourcePaths">Relative or absolute paths to source files or directories,
    /// resolved against the test assembly's directory.</param>
    public AtlasDataFilesAttribute(params string[] sourcePaths) => SourcePaths = sourcePaths;

    /// <summary>Gets the source paths declared by this attribute.</summary>
    public string[] SourcePaths { get; }

    /// <summary>Gets or sets the directory under the data path the sources are copied into,
    /// e.g. <c>"ModConfig"</c>. Empty (the default) targets the data path root. Must stay inside
    /// the data path: rooted paths and <c>..</c> segments that escape it fail the boot.</summary>
    public string TargetPath { get; set; } = string.Empty;
}
