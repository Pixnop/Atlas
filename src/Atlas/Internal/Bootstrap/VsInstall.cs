using System.Reflection;
using System.Security.Cryptography;
using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>Locates and verifies the Vintage Story installation and the consumer's setup.</summary>
internal static class VsInstall
{
    /// <summary>Locates the Vintage Story installation directory from the VINTAGE_STORY environment variable.</summary>
    /// <returns>The installation directory path.</returns>
    /// <exception cref="AtlasSetupException">Thrown when VINTAGE_STORY is not set or does not contain VintagestoryLib.dll.</exception>
    public static string Locate()
    {
        string? dir = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "VintagestoryLib.dll")))
        {
            throw new AtlasSetupException(
                "VINTAGE_STORY must point at a Vintage Story install containing VintagestoryLib.dll " +
                $"(current value: '{dir ?? "<unset>"}')");
        }

        return dir;
    }

    /// <summary>Verifies that a VintagestoryAPI.dll present in the consumer test output ships with
    /// its VintagestoryAPI.pdb next to it.</summary>
    /// <param name="testOutputDir">The consumer test project's output directory (the assembly
    /// probing base): <see cref="AppContext.BaseDirectory"/> captured BEFORE
    /// <see cref="GameEnvironment.Initialize"/> redirects it to the install directory. On hosts
    /// booted after that redirect the check runs against the install directory instead, which is
    /// harmless: the game ships its pdb (its own logger depends on it, see remarks).</param>
    /// <exception cref="AtlasSetupException">Thrown when VintagestoryAPI.dll is present in the
    /// directory without VintagestoryAPI.pdb next to it.</exception>
    /// <remarks>A VintagestoryAPI.dll copy in the test output wins default assembly probing over
    /// the game install's copy. The game's LoggerBase static constructor derives its SourcePath by
    /// deliberately throwing a dummy exception and reading the throw site's source file name from
    /// <c>new StackTrace(e, fNeedFileInfo: true)</c> - information that only exists when the pdb
    /// sits next to the loaded dll. Without it, GetFileName() returns null and the boot dies in an
    /// opaque TypeInitializationException (NullReferenceException in LoggerBase..cctor) at the
    /// first ServerLogger construction (verified by decompiling Vintage Story 1.22.0). Failing
    /// here turns that into an actionable setup error before the engine is ever touched.</remarks>
    public static void VerifyApiPdbPresent(string testOutputDir)
    {
        if (File.Exists(Path.Combine(testOutputDir, "VintagestoryAPI.dll"))
            && !File.Exists(Path.Combine(testOutputDir, "VintagestoryAPI.pdb")))
        {
            throw new AtlasSetupException(
                $"VintagestoryAPI.dll is present in the test output directory ('{testOutputDir}') " +
                "without VintagestoryAPI.pdb next to it. The game's logger derives source paths from " +
                "pdb debug info during type initialization, so booting the embedded server would fail " +
                "with an opaque TypeInitializationException (NullReferenceException in " +
                "LoggerBase..cctor). Ship the matching VintagestoryAPI.pdb next to the dll (a plain " +
                "<Reference> with a HintPath into the game install copies both automatically), or stop " +
                "copying the dll (<Private>false</Private>) so the game install's own copy is loaded.");
        }
    }

    /// <summary>Verifies that a VintagestoryAPI.dll present in the consumer test output is
    /// content-identical (byte comparison via hash; versions are display-only, forks rebuild at
    /// the same version) to the copy shipped by the VINTAGE_STORY install (issue #49).</summary>
    /// <param name="testOutputDir">The consumer test project's output directory (the assembly
    /// probing base): <see cref="AppContext.BaseDirectory"/> captured BEFORE
    /// <see cref="GameEnvironment.Initialize"/> redirects it to the install directory. On hosts
    /// booted after that redirect the check compares the install copy with itself, which is
    /// harmless.</param>
    /// <param name="installDir">The install directory returned by <see cref="Locate"/>.</param>
    /// <exception cref="AtlasSetupException">Thrown when both copies exist and their content
    /// differs.</exception>
    /// <remarks>The test-output copy wins default assembly probing over the install's copy (the
    /// JIT needs it before Atlas's AssemblyResolve hook exists, see build/Atlas.E2E.targets).
    /// Repointing VINTAGE_STORY at a different install without rebuilding therefore loads a mixed
    /// set: the target install's VintagestoryLib against the stale local VintagestoryAPI, which
    /// dies deep into boot with a cryptic MissingFieldException/MissingMethodException (observed
    /// as 46/56 scenario failures against the Stratum fork, whose API adds fields at the same
    /// assembly version). Failing here turns that into an actionable setup error before the
    /// engine is ever touched.</remarks>
    public static void VerifyApiCopyMatchesInstall(string testOutputDir, string installDir)
    {
        string localPath = Path.Combine(testOutputDir, "VintagestoryAPI.dll");
        if (!File.Exists(localPath))
        {
            // No local copy (consumers referencing with Private=false): probing falls through to
            // the install's own copy, so nothing can diverge.
            return;
        }

        string installPath = Path.Combine(installDir, "VintagestoryAPI.dll");
        if (!File.Exists(installPath))
        {
            // Locate() already vetted the install (VintagestoryLib.dll present); an install
            // without VintagestoryAPI.dll leaves nothing to compare against, and any later boot
            // failure then points at the broken install rather than at a stale local copy.
            return;
        }

        ApiCopySync.FileIdentity local = ReadIdentity(localPath);
        ApiCopySync.FileIdentity install = ReadIdentity(installPath);
        if (!ApiCopySync.AreIdentical(local, install))
        {
            throw new AtlasSetupException(
                ApiCopySync.DescribeMismatch(localPath, local, installPath, install));
        }
    }

    /// <summary>Reads the content identity of one file: size, SHA-256, and (when the file is a
    /// readable assembly) its assembly version.</summary>
    /// <param name="path">The file to identify.</param>
    /// <returns>The identity.</returns>
    private static ApiCopySync.FileIdentity ReadIdentity(string path)
    {
        long length;
        string hash;
        using (FileStream stream = File.OpenRead(path))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream));
            length = stream.Length;
        }

        string? version;
        try
        {
            version = AssemblyName.GetAssemblyName(path).Version?.ToString();
        }
        catch (BadImageFormatException)
        {
            // Not a .NET assembly (or unreadable metadata): identity still works on size + hash,
            // the message just reports the version as unknown.
            version = null;
        }

        return new ApiCopySync.FileIdentity(length, hash, version);
    }
}
