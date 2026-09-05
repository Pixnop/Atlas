namespace Atlas.Api;

/// <summary>A file or directory to copy into the embedded server's scratch data path before it
/// boots, so files a mod reads during startup (e.g. <c>api.LoadModConfig</c> from
/// <c>ModConfig/</c> in <c>StartServerSide</c>) are already in place.</summary>
/// <param name="SourcePath">Relative or absolute path to the source file or directory. Relative
/// paths resolve against the same base directory as mod paths (for scenario classes, the test
/// assembly's directory). A directory's <em>contents</em> are copied, not the directory itself.</param>
/// <param name="TargetPath">Directory under the data path to copy into, e.g. <c>"ModConfig"</c>.
/// Empty (the default) targets the data path root, so a directory source laid out like the data
/// path itself (a <c>ModConfig</c> folder, a <c>Macros</c> folder) is overlaid onto it. Must stay
/// inside the data path: rooted paths and <c>..</c> segments that escape it are rejected.</param>
/// <remarks>A scenario class does not build this record itself: it declares
/// <c>[AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]</c> on the class or the
/// assembly, and the xUnit adapter turns each declared source path into one seed. Assembly-level
/// attributes are mapped first, class-level ones after, so a class-level seed wins a file name
/// collision.</remarks>
public sealed record DataFileSeed(string SourcePath, string TargetPath = "");
