namespace Atlas.Internal.Staging;

/// <summary>The pure decision core of the staged-mod binding check (issue #170): decides, from
/// assembly identities alone, whether the assembly the engine bound for a staged mod is the build
/// Atlas staged, and formats the setup error when it is not. Kept free of IO and engine types so
/// the comparison and the message are testable without files, a boot or a loaded mod; reading the
/// identities and walking the mod list stay in the thin shell (<see cref="StagedModVerifier"/>).</summary>
/// <remarks>Why this can go wrong at all: the engine loads a code mod through
/// <c>Assembly.UnsafeLoadFrom</c>, and the runtime resolves that against the process's own
/// probing set first. When a same-named assembly sits there (the copy a <c>ProjectReference</c>
/// to the mod puts next to the test assembly), it is handed back for any staged file of the same
/// name and version, without a look at the staged file's content (measured on .NET 10, and the
/// engine loads every supported version's mods this way). A mod that keeps a frozen
/// <c>AssemblyVersion</c> shares one identity across its builds, so a second build staged next to
/// a referenced one is ignored without a word. The module version id (MVID) is what tells the
/// builds apart: it is fresh per compilation, is read from the file's metadata without loading
/// it, and is the same for a byte-identical copy at any path, so a legitimate layout (the staged
/// file is a copy of the referenced build) never trips it.</remarks>
internal static class StagedModBinding
{
    /// <summary>Stands in for a loaded assembly's path when it has none (an image loaded from
    /// bytes).</summary>
    internal const string InMemoryImage = "<in-memory image>";

    private const string WikiUrl = "https://github.com/Pixnop/Atlas/wiki/Mod-Staging#testing-two-builds-of-the-same-mod";

    /// <summary>Decides whether the bound assembly is one of the staged files, and describes the
    /// mismatch when it is not.</summary>
    /// <param name="modName">The mod's id (or its file name when it has none), for the message.</param>
    /// <param name="staged">The managed dlls found in the staged mod: the dll itself, or the
    /// root-level dlls of its folder or zip.</param>
    /// <param name="loaded">The assembly the engine bound for the mod's systems.</param>
    /// <returns>The setup error message, or <see langword="null"/> when nothing is wrong: the
    /// bound assembly is one of the staged files, or none of them shares its simple name (then it
    /// was not loaded from this staging at all, which this check has nothing to say about).</returns>
    public static string? Verify(string modName, IReadOnlyList<AssemblyFile> staged, AssemblyFile loaded)
    {
        ArgumentNullException.ThrowIfNull(staged);
        AssemblyFile? sameName = null;
        foreach (AssemblyFile file in staged)
        {
            if (!string.Equals(file.SimpleName, loaded.SimpleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Mvid == loaded.Mvid)
            {
                return null;
            }

            sameName ??= file;
        }

        return sameName is { } other ? Describe(modName, other, loaded) : null;
    }

    /// <summary>Formats the setup error for a staged mod whose bound assembly is another build of
    /// the same identity.</summary>
    /// <param name="modName">The mod's id, or its file name.</param>
    /// <param name="staged">The staged file that has the loaded assembly's name and a different
    /// MVID.</param>
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

    /// <summary>One managed assembly file: where it is and which build of which assembly it holds.</summary>
    /// <param name="Path">The file's path; for a dll inside a zip, the zip's path, then
    /// <c>!/</c>, then the entry name. For a loaded assembly without a file,
    /// <see cref="InMemoryImage"/>.</param>
    /// <param name="SimpleName">The assembly's simple name.</param>
    /// <param name="Mvid">The module version id: fresh per compilation, so two builds of one
    /// assembly identity differ here.</param>
    internal readonly record struct AssemblyFile(string Path, string SimpleName, Guid Mvid);
}
