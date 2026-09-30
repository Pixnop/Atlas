namespace Atlas.Internal.Staging;

/// <summary>The pure decision core of the staged-mod binding check (issue #170): decides, from
/// assembly identities alone, whether the assembly the engine bound for a staged mod is the build
/// Atlas staged, formats the setup error when it is not, and formats the one-line notice for every
/// other outcome so a run that compared a dll reads differently from a run that compared nothing.
/// Kept free of IO and engine types so the comparison and the messages are testable without
/// files, a boot or a loaded mod; reading the identities and walking the mod list stay in the thin
/// shell (<see cref="StagedModVerifier"/>).</summary>
/// <remarks>What it covers: a code mod staged as a dll, a folder or a zip that ships a dll at its
/// root. A source mod (compiled by the engine, so there is no staged dll to compare) and a
/// content-only mod (no <c>ModSystem</c>) are exempt, and say so in a "skipped" notice; the
/// bridge and the game's own mods are not staged mods and are not reported at all.
/// <para>Why this can go wrong at all: the engine loads a code mod through
/// <c>Assembly.UnsafeLoadFrom</c>, and the runtime resolves that against the process's own
/// probing set first. When a same-named assembly sits there (the copy a <c>ProjectReference</c>
/// to the mod puts next to the test assembly), it is handed back for any staged file of the same
/// name and version, without a look at the staged file's content (measured on .NET 10, and the
/// engine loads every supported version's mods this way). A mod that keeps a frozen
/// <c>AssemblyVersion</c> shares one identity across its builds, so a second build staged next to
/// a referenced one is ignored without a word. The module version id (MVID) is what tells the
/// builds apart: it is fresh per compilation, is read from the file's metadata without loading
/// it, and is the same for a byte-identical copy at any path, so a legitimate layout (the staged
/// file is a copy of the referenced build) never trips it.</para></remarks>
internal static class StagedModBinding
{
    /// <summary>Stands in for a loaded assembly's path when it has none (an image loaded from
    /// bytes).</summary>
    internal const string InMemoryImage = "<in-memory image>";

    private const string WikiUrl = "https://github.com/Pixnop/Atlas/wiki/Mod-Staging#testing-two-builds-of-the-same-mod";

    /// <summary>Decides whether the bound assembly is one of the staged files, and describes the
    /// outcome either way.</summary>
    /// <param name="modName">The mod's id (or its file name when it has none), for the message.</param>
    /// <param name="staged">The managed dlls found in the staged mod: the dll itself, or the
    /// root-level dlls of its folder or zip. Empty for a source mod.</param>
    /// <param name="loaded">The assembly the engine bound for the mod's systems, or
    /// <see langword="null"/> when no system was loaded from the mod (a content-only mod).</param>
    /// <returns>A mismatch with the setup error when the bound assembly is another build of a
    /// staged file's identity; otherwise the notice to log: verified when the bound assembly is
    /// one of the staged files, or skipped, with the reason, when there was nothing to compare
    /// (no bound assembly, no staged dll, or none sharing the bound assembly's simple name, in
    /// which case it was not loaded from this staging at all).</returns>
    public static Verdict Verify(string modName, IReadOnlyList<AssemblyFile> staged, AssemblyFile? loaded)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (loaded is not { } bound)
        {
            return Skipped(modName, "not a code mod (no ModSystem was loaded from it)");
        }

        if (staged.Count == 0)
        {
            return Skipped(modName, "no staged dll at its root (a source mod, compiled by the engine)");
        }

        AssemblyFile? sameName = null;
        foreach (AssemblyFile file in staged)
        {
            if (!string.Equals(file.SimpleName, bound.SimpleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Mvid == bound.Mvid)
            {
                return new Verdict(false, $"[Atlas] staged mod '{modName}': verified (MVID {bound.Mvid})");
            }

            sameName ??= file;
        }

        return sameName is { } other
            ? new Verdict(true, Describe(modName, other, bound))
            : Skipped(modName, $"no staged dll is named '{bound.SimpleName}', the assembly the engine bound");
    }

    /// <summary>Maps a path inside a staged mod back to the path the mod was staged from, so a
    /// message names the file the user pointed Atlas at instead of Atlas's scratch copy.</summary>
    /// <param name="stagedPath">A path under <paramref name="stagedRoot"/>: the root itself, a
    /// file inside a staged folder, or an entry inside a staged zip (<c>!/</c> then the entry
    /// name).</param>
    /// <param name="stagedRoot">The staged dll, folder or zip.</param>
    /// <param name="sourceRoot">The path it was copied from.</param>
    /// <returns><paramref name="stagedPath"/> with <paramref name="stagedRoot"/> replaced by
    /// <paramref name="sourceRoot"/>; the path unchanged when it is not under the root (a
    /// sibling that merely shares the root's leading characters is not).</returns>
    public static string ToSourcePath(string stagedPath, string stagedRoot, string sourceRoot)
    {
        if (!stagedPath.StartsWith(stagedRoot, StringComparison.Ordinal))
        {
            return stagedPath;
        }

        string rest = stagedPath[stagedRoot.Length..];
        bool underRoot = rest.Length == 0
            || rest[0] == '!'
            || rest[0] == Path.DirectorySeparatorChar
            || rest[0] == Path.AltDirectorySeparatorChar;
        return underRoot ? sourceRoot + rest : stagedPath;
    }

    /// <summary>Formats the setup error for a staged mod whose bound assembly is another build of
    /// the same identity.</summary>
    /// <param name="modName">The mod's id, or its file name.</param>
    /// <param name="staged">The staged file that has the loaded assembly's name and a different
    /// MVID; its path is the one the mod was staged from when the shell knows it (see
    /// <see cref="ToSourcePath"/>).</param>
    /// <param name="loaded">The assembly the engine bound instead.</param>
    /// <returns>The complete error message.</returns>
    public static string Describe(string modName, AssemblyFile staged, AssemblyFile loaded)
    {
        return
            $"Mod '{modName}' was staged from '{staged.Path}' (MVID {staged.Mvid}), but the engine is " +
            $"running another build of its assembly '{loaded.SimpleName}', loaded from '{loaded.Path}' " +
            $"(MVID {loaded.Mvid}). The process binds one copy per assembly identity (name and " +
            "version), so a second build that keeps the same AssemblyVersion is ignored in favor of " +
            "the one bound first, usually the copy a ProjectReference put next to the test assembly. " +
            $"Stage and reference one build of the mod per test project (see {WikiUrl}).";
    }

    private static Verdict Skipped(string modName, string reason)
        => new(false, $"[Atlas] staged mod '{modName}': skipped, {reason}");

    /// <summary>What the check concluded for one staged mod.</summary>
    /// <param name="Mismatch"><see langword="true"/> when the bound assembly is another build of a
    /// staged file's identity and the boot must fail.</param>
    /// <param name="Text">The setup error when <paramref name="Mismatch"/> is set; otherwise the
    /// one-line "[Atlas] ..." notice: verified, or skipped with the reason.</param>
    internal readonly record struct Verdict(bool Mismatch, string Text);

    /// <summary>One managed assembly file: where it is and which build of which assembly it holds.</summary>
    /// <param name="Path">The file's path; for a dll inside a zip, the zip's path, then
    /// <c>!/</c>, then the entry name. For a loaded assembly without a file,
    /// <see cref="InMemoryImage"/>.</param>
    /// <param name="SimpleName">The assembly's simple name.</param>
    /// <param name="Mvid">The module version id: fresh per compilation, so two builds of one
    /// assembly identity differ here.</param>
    internal readonly record struct AssemblyFile(string Path, string SimpleName, Guid Mvid);
}
