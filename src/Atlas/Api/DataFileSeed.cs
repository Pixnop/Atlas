namespace Atlas.Api;

/// <summary>A file or directory to copy into the embedded server's scratch data path before it
/// boots, so files a mod reads during startup (e.g. <c>api.LoadModConfig</c> from
/// <c>ModConfig/</c> in <c>StartServerSide</c>) are already in place.</summary>
/// <param name="SourcePath">Relative or absolute path to the source file or directory. Relative
/// paths resolve against the same base directory as mod paths (for scenario classes, the test
/// assembly's directory). A directory's <em>contents</em> are copied, not the directory itself.</param>
/// <param name="TargetPath">Directory under the data path to copy into, e.g. <c>"ModConfig"</c>.
/// Empty (the default) targets the data path root, so a directory source laid out like the data
/// path itself (<c>ModConfig/…</c>, <c>Macros/…</c>) is overlaid onto it. Must stay inside the
/// data path: rooted paths and <c>..</c> segments that escape it are rejected.</param>
public sealed record DataFileSeed(string SourcePath, string TargetPath = "");
