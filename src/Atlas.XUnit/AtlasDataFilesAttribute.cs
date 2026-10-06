namespace Atlas.XUnit;

/// <summary>Declares files to seed into the embedded server's scratch data path before it boots,
/// so files a mod reads during startup (most commonly <c>api.LoadModConfig("mymod.json")</c>
/// from <c>ModConfig/</c> in <c>StartServerSide</c>) are already in place.</summary>
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
/// [AtlasDataFiles("fixtures/serverdata")]   // contains ModConfig/mymod.json, Macros/mymacro.json, etc.
/// </code>
/// <para>Assembly-level attributes apply to every scenario class; class-level attributes are
/// copied after them, so on a file name collision the class-level seed wins.</para>
/// <para>Files are copied as they are, with one exception: a file that holds
/// <c>{{atlas:port:NAME}}</c> gets a free loopback port in its place. A mod that listens on a
/// port reads it from its config, and a port frozen in the fixture is shared by every run: the
/// test platform takes ephemeral ports on every run (<c>vstest.console</c> listens on one, the
/// test host connects from another, and that socket lingers in TIME_WAIT for about a minute), and
/// when one lands on the fixture port the mod cannot bind and the whole class fails; two runs on
/// one loopback fight over it as well. Write the token where the number goes and ask Atlas which
/// port it chose:</para>
/// <code>
/// // fixtures/ModConfig/mymod.json (not valid JSON until it is seeded)
/// { "metricsPort": {{atlas:port:metrics}} }
///
/// // in a scenario
/// int port = World.DataFilePort("metrics");
/// </code>
/// <para><c>NAME</c> is made of letters, digits, <c>_</c>, <c>.</c> and <c>-</c>, and is case
/// sensitive. Atlas draws the port when it seeds, once per host: every file that names the same
/// token gets the same number, two names get two numbers, and a host that boots again (a
/// <c>FreshWorld</c> or <c>RestartWorld</c> scenario) seeds again and draws again. The port is
/// free on 127.0.0.1 for TCP and UDP at that moment and is not reserved: nothing holds it
/// afterwards, and a mod binds it during its startup, seconds after the draw. Another process can
/// take it first, and two processes can in principle draw the same number (on Linux, two draws made
/// one after the other returned the same port about once in 5000). A scenario that needs an
/// address that refuses connections should not rely on the port staying free. A token is resolved in any file whose content is UTF-8 text,
/// whatever its extension; the BOM and the line endings stay as they are. A file that holds the
/// token prefix but is not UTF-8, or a <c>{{atlas:</c> token that is not a port token, fails the
/// boot naming the file. Files with no token are copied byte for byte.</para>
/// <para>A port you freeze in the fixture yourself still works, for a number that something
/// outside the test has to know in advance. Keep it below 32768, outside the ephemeral range of
/// both Linux (32768 to 60999 by default) and Windows (49152 to 65535 by default), and run one
/// suite at a time.</para>
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
