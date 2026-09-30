using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Staging;

/// <summary>The thin IO shell around <see cref="StagedModBinding"/> (issue #170): reads the
/// identity of each staged mod's dll straight from the file's metadata, without loading it, reads
/// the identity of the assembly the engine bound for that mod, and fails the boot when they are
/// two builds.</summary>
/// <remarks>Cost: once per boot, per staged code mod that has a <c>ModSystem</c>, one metadata
/// read of each root-level dll it ships (a zip's dlls are inflated into memory first). Nothing is
/// loaded, so the check cannot itself bind a build.</remarks>
internal static class StagedModVerifier
{
    /// <summary>Checks every mod the engine loaded from <paramref name="stagingDir"/>.</summary>
    /// <param name="mods">The mods the engine loaded, as <c>ICoreAPI.ModLoader.Mods</c> lists them.</param>
    /// <param name="stagingDir">The folder <see cref="ModStager"/> staged the mods-under-test
    /// into; a mod loaded from anywhere else (the bridge, the game's own mods) is not Atlas's to
    /// vouch for and is skipped.</param>
    /// <exception cref="AtlasSetupException">Thrown when a staged mod's bound assembly is another
    /// build than the staged file; the message names every such mod.</exception>
    public static void VerifyAll(IEnumerable<Mod> mods, string stagingDir)
    {
        string root = Path.GetFullPath(stagingDir);
        List<string> errors = [];
        foreach (Mod mod in mods)
        {
            // A mod with no ModSystem (a client-only or content mod) has no bound type to read
            // the assembly off, so there is nothing to compare.
            if (mod.SourceType is not (EnumModSourceType.DLL or EnumModSourceType.ZIP or EnumModSourceType.Folder)
                || !string.Equals(Path.GetDirectoryName(mod.SourcePath), root, StringComparison.OrdinalIgnoreCase)
                || mod.Systems.FirstOrDefault() is not { } system)
            {
                continue;
            }

            string? error = StagedModBinding.Verify(
                mod.Info?.ModID ?? mod.FileName,
                ReadStaged(mod.SourceType, mod.SourcePath),
                DescribeLoaded(system.GetType().Assembly));
            if (error != null)
            {
                errors.Add(error);
            }
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
