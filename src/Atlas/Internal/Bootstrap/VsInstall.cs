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
}
