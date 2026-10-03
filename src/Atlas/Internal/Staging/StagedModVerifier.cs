using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Staging;

/// <summary>The thin IO shell around <see cref="StagedModBinding"/> (issue #170): reads the
/// identity of each staged mod's dll straight from the file's metadata, without loading it, reads
/// the identity of the assembly the engine bound for that mod, fails the boot when they are two
/// builds, and logs one line per staged mod otherwise.</summary>
/// <remarks>Covers every mod the engine loaded from the staging folder: a code mod staged as a
/// dll, a folder or a zip is compared; a source mod (compiled by the engine, no staged dll) and a
/// content-only mod (no <c>ModSystem</c>) are exempt and logged as skipped. The bridge and the
/// game's own mods load from elsewhere and are not reported.
/// <para>A folder or zip mod's libraries (its other root-level dlls) are compared once its own dll
/// verified, each with the assembly of its name the process holds (see
/// <see cref="StagedModBinding.VerifyDependency"/>). The engine loads every root-level dll of such
/// a mod when it loads the mod (a folder or zip mod has a <c>modinfo.json</c>, so no single
/// assembly is selected), whether the mod uses it or not, so each one is compared. The check
/// loads nothing itself. A library the process does not hold at that point is reported as
/// skipped, a fallback and not the outcome for an unused library. A library the game ships itself
/// is skipped without a line: its copy binds before any mod folder, in the real game too.</para>
/// <para>A staged mod the engine did not load at all is looked at only when the engine logged a
/// same-name refusal for one of its dlls (a second build of an assembly the process already has,
/// see <see cref="StagedModBinding"/>): a staged mod that is absent for any other reason is left
/// to the engine's own error, so a test that stages a deliberately broken mod still boots.</para>
/// Cost: once per boot, per staged code mod that has a <c>ModSystem</c>, one metadata
/// read of each root-level dll it ships (a zip's dlls are inflated into memory first), plus the
/// same for a staged mod missing from the mod list when a refusal was logged. Nothing is
/// loaded, so the check cannot itself bind a build.</remarks>
internal static class StagedModVerifier
{
    /// <summary>Checks every mod the engine loaded from <paramref name="stagingDir"/>.</summary>
    /// <param name="mods">The mods the engine loaded, as <c>ICoreAPI.ModLoader.Mods</c> lists them.</param>
    /// <param name="stagingDir">The folder <see cref="ModStager"/> staged the mods-under-test
    /// into; a mod loaded from anywhere else (the bridge, the game's own mods) is not Atlas's to
    /// vouch for and is skipped without a line.</param>
    /// <param name="sources">Where each staged mod was copied from, as <see cref="ModStager.Stage"/>
    /// returns it, so a mismatch names the path the user gave and not Atlas's scratch copy. A mod
    /// missing from it is named by its staged path.</param>
    /// <param name="log">Receives the one-line notice of every staged mod that did not fail:
    /// verified, or skipped with the reason.</param>
    /// <param name="owner">The scenario class whose boot this is, named on every notice;
    /// <see langword="null"/> for a host no scenario class owns.</param>
    /// <param name="engineErrors">What the engine logged at Warning level or above during the
    /// boot (<c>BootDiagnosticsLog.Snapshot</c>), read to tell a staged mod the engine refused
    /// because an assembly of the same name was already loaded; <see langword="null"/> skips that
    /// part.</param>
    /// <param name="hostScratch">This boot's scratch folder, one of several under a shared root.
    /// The runtime binds an assembly identity once per process, so for every boot after the first
    /// the bound copy is the one an earlier boot staged, in that boot's scratch folder (possibly
    /// deleted by now); a bound copy under a sibling of this folder is reported as such.
    /// <see langword="null"/> never reports it.</param>
    /// <param name="install">The Vintage Story install the server runs from, read to tell a library
    /// the game ships itself from one the mod brings: the game's copy is bound before any mod
    /// folder, so a mod's own copy of it is never compared. <see langword="null"/> treats none as
    /// the game's.</param>
    /// <exception cref="AtlasSetupException">Thrown when a staged mod's bound assembly is another
    /// build than the staged file, or when the engine refused a staged build because another
    /// build of its assembly was already loaded; the message names every such mod.</exception>
    public static void VerifyAll(
        IEnumerable<Mod> mods,
        string stagingDir,
        IReadOnlyDictionary<string, string> sources,
        Action<string> log,
        string? owner = null,
        IReadOnlyList<BootDiagnosticEntry>? engineErrors = null,
        string? hostScratch = null,
        string? install = null)
    {
        string root = Path.GetFullPath(stagingDir);
        List<string> errors = [];
        HashSet<string> loadedPaths = [];
        foreach (Mod mod in mods)
        {
            loadedPaths.Add(mod.SourcePath);
            if (!string.Equals(Path.GetDirectoryName(mod.SourcePath), root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A mod with no ModSystem (a content-only mod) has no bound type to read the
            // assembly off, so there is nothing to read or compare; Verify says so.
            StagedModBinding.AssemblyFile? loaded =
                mod.Systems.FirstOrDefault() is { } system ? DescribeLoaded(system.GetType().Assembly) : null;
            IReadOnlyList<StagedModBinding.AssemblyFile> staged = loaded is null ? [] : ReadStaged(mod.SourceType, mod.SourcePath);
            if (sources.TryGetValue(mod.SourcePath, out string? source))
            {
                staged = [.. staged.Select(file => file with { Path = StagedModBinding.ToSourcePath(file.Path, mod.SourcePath, source) })];
            }

            bool earlierBoot = loaded is { } binding && IsUnderSiblingOf(binding.Path, hostScratch);
            StagedModBinding.Verdict verdict = StagedModBinding.Verify(mod.Info?.ModID ?? mod.FileName, staged, loaded, owner, earlierBoot);
            if (verdict.Mismatch)
            {
                errors.Add(verdict.Text);
                continue;
            }

            log(verdict.Text);
            if (verdict is { Verified: true } && loaded is { } bound)
            {
                VerifyDependencies(mod.Info?.ModID ?? mod.FileName, staged, bound, owner, hostScratch, install, log, errors);
            }
        }

        if (engineErrors is { Count: > 0 })
        {
            AddRefusals(errors, root, loadedPaths, sources, engineErrors);
        }

        if (errors.Count > 0)
        {
            throw new AtlasSetupException(string.Join('\n', errors));
        }
    }

    /// <summary>Reads the managed dlls a staged mod ships, the way the engine picks its code:
    /// the dll itself, or the dlls at the root of the folder or zip (a dll in a subfolder is never
    /// the mod's code).</summary>
    /// <param name="sourceType">How the mod was staged.</param>
    /// <param name="sourcePath">The staged dll, folder or zip.</param>
    /// <returns>One entry per readable managed dll; a file that is not one is left out, so an
    /// unreadable candidate can only ever hide a mismatch, never invent one.</returns>
    public static IReadOnlyList<StagedModBinding.AssemblyFile> ReadStaged(EnumModSourceType sourceType, string sourcePath)
    {
        List<StagedModBinding.AssemblyFile> files = [];
        try
        {
            switch (sourceType)
            {
                case EnumModSourceType.DLL:
                    Add(files, ReadFile(sourcePath));
                    break;
                case EnumModSourceType.Folder:
                    foreach (string dll in Directory.EnumerateFiles(sourcePath, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        Add(files, ReadFile(dll));
                    }

                    break;
                case EnumModSourceType.ZIP:
                    using (ZipArchive zip = ZipFile.OpenRead(sourcePath))
                    {
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                && entry.FullName.IndexOfAny(['/', '\\']) < 0)
                            {
                                Add(files, ReadEntry(entry, sourcePath + "!/" + entry.FullName));
                            }
                        }
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The engine already unpacked and loaded this mod, so a folder or zip that cannot be
            // read now is a race with the file system, not a finding: keep what was read.
        }

        return files;
    }

    /// <summary>Reads one dll's simple name and MVID from its metadata, without loading it.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file's identity, or <see langword="null"/> when it is missing or not a managed
    /// assembly.</returns>
    public static StagedModBinding.AssemblyFile? ReadFile(string path)
    {
        try
        {
            return Read(File.OpenRead(path), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The libraries staged next to the mod's own dll, other than the game's own: each is compared
    // with the assembly of its name the process holds, once the mod's dll itself verified. One line
    // per library, in name order so a boot's lines do not depend on the file system's order.
    private static void VerifyDependencies(
        string modName,
        IReadOnlyList<StagedModBinding.AssemblyFile> staged,
        StagedModBinding.AssemblyFile bound,
        string? owner,
        string? hostScratch,
        string? install,
        Action<string> log,
        List<string> errors)
    {
        IEnumerable<StagedModBinding.AssemblyFile> libraries = staged
            .Where(file => !string.Equals(file.SimpleName, bound.SimpleName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.SimpleName, StringComparer.Ordinal);
        foreach (StagedModBinding.AssemblyFile library in libraries)
        {
            StagedModBinding.AssemblyFile? loaded = FindLoaded(library.SimpleName);
            if (ProvidedByTheGame(library.SimpleName, loaded, install))
            {
                continue;
            }

            StagedModBinding.Verdict verdict = StagedModBinding.VerifyDependency(
                modName, library, loaded, owner, loaded is { } found && IsUnderSiblingOf(found.Path, hostScratch));
            if (verdict.Mismatch)
            {
                errors.Add(verdict.Text);
            }
            else
            {
                log(verdict.Text);
            }
        }
    }

    // Whether the game binds a library of that name before it ever looks in a mod folder, in the
    // real game as much as in a test: its own install holds one (the root or Lib), or the copy the
    // process holds lives in the runtime's own folder. A mod's copy of such a library is ignored by
    // the game too, so there is nothing to compare and nothing to report. The install's Mods folder
    // (VSEssentials, VSSurvivalMod, VSCreativeMod) is deliberately not looked at: a mod shipping
    // another build of one of those is refused by the engine in the real game, so it is compared.
    private static bool ProvidedByTheGame(string simpleName, StagedModBinding.AssemblyFile? loaded, string? install)
    {
        string file = simpleName + ".dll";
        if (install is not null && (File.Exists(Path.Combine(install, file)) || File.Exists(Path.Combine(install, "Lib", file))))
        {
            return true;
        }

        return loaded is { } found
            && IsUnder(found.Path, Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory()));
    }

    // Whether the path lies in a folder next to this boot's scratch folder (another boot's scratch)
    // rather than in this boot's own or somewhere unrelated.
    private static bool IsUnderSiblingOf(string path, string? hostScratch)
    {
        if (hostScratch is null)
        {
            return false;
        }

        string own = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostScratch));
        return Path.GetDirectoryName(own) is { } shared && IsUnder(path, shared) && !IsUnder(path, own);
    }

    private static bool IsUnder(string path, string folder)
        => path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // The staged mods the engine left out of the mod list, checked against what it logged.
    private static void AddRefusals(
        List<string> errors,
        string root,
        HashSet<string> loadedPaths,
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyList<BootDiagnosticEntry> engineErrors)
    {
        foreach ((string stagedPath, string source) in sources)
        {
            if (loadedPaths.Contains(stagedPath)
                || !string.Equals(Path.GetDirectoryName(stagedPath), root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            StagedModBinding.AssemblyFile[] staged =
            [
                .. ReadStaged(SourceTypeOf(stagedPath), stagedPath)
                    .Select(file => file with { Path = StagedModBinding.ToSourcePath(file.Path, stagedPath, source) }),
            ];
            if (StagedModBinding.DescribeRefusal(Path.GetFileName(stagedPath), staged, engineErrors, FindLoaded) is { } refusal)
            {
                errors.Add(refusal);
            }
        }
    }

    // How ModStager staged it: a directory is a folder mod, a .zip a zip, anything else a lone
    // file (a dll, or a source file that ReadStaged leaves alone).
    private static EnumModSourceType SourceTypeOf(string stagedPath)
        => Directory.Exists(stagedPath) ? EnumModSourceType.Folder
            : stagedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? EnumModSourceType.ZIP
            : EnumModSourceType.DLL;

    // The assembly of that simple name the process already holds, if any. Dynamic assemblies have
    // no file to describe.
    private static StagedModBinding.AssemblyFile? FindLoaded(string simpleName)
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic
                && string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            .Select(DescribeLoaded)
            .Cast<StagedModBinding.AssemblyFile?>()
            .FirstOrDefault();

    private static StagedModBinding.AssemblyFile? ReadEntry(ZipArchiveEntry entry, string displayPath)
    {
        try
        {
            // A zip entry's stream cannot seek, and the metadata reader needs to.
            var image = new MemoryStream();
            using (Stream inflated = entry.Open())
            {
                inflated.CopyTo(image);
            }

            image.Position = 0;
            return Read(image, displayPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return null;
        }
    }

    // Takes ownership of the stream.
    private static StagedModBinding.AssemblyFile? Read(Stream image, string path)
    {
        try
        {
            using var pe = new PEReader(image);
            if (!pe.HasMetadata)
            {
                return null;
            }

            MetadataReader metadata = pe.GetMetadataReader();
            return metadata.IsAssembly
                ? new StagedModBinding.AssemblyFile(
                    path,
                    metadata.GetString(metadata.GetAssemblyDefinition().Name),
                    metadata.GetGuid(metadata.GetModuleDefinition().Mvid))
                : null;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static StagedModBinding.AssemblyFile DescribeLoaded(Assembly assembly)
        => new(
            assembly.Location.Length == 0 ? StagedModBinding.InMemoryImage : assembly.Location,
            assembly.GetName().Name ?? string.Empty,
            assembly.ManifestModule.ModuleVersionId);

    private static void Add(List<StagedModBinding.AssemblyFile> files, StagedModBinding.AssemblyFile? file)
    {
        if (file is { } found)
        {
            files.Add(found);
        }
    }
}
