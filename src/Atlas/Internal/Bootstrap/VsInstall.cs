using System.Globalization;
using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>Locates and verifies the Vintage Story installation and the consumer's setup. The
/// single owner of what VINTAGE_STORY must hold and of the sentence that says so when it does
/// not: <see cref="Locate"/> is the boot path, and everything else that reads the variable
/// (<see cref="GameEnvironment"/>'s resolve hook, <see cref="EngineStager"/>'s preflight, the
/// CLI's own pre-run check) goes through the constants and <see cref="Validate"/> here.</summary>
internal static class VsInstall
{
    /// <summary>Name of the environment variable pointing at the Vintage Story install.</summary>
    public const string VariableName = "VINTAGE_STORY";

    /// <summary>The file whose presence makes a directory a Vintage Story install rather than
    /// just a directory: the engine assembly the embedded server boots from.</summary>
    public const string LibraryFileName = "VintagestoryLib.dll";

    /// <summary>What to tell the reader when the variable does not point at an install, with
    /// <c>{0}</c> for the value it did hold. A const, so Atlas.Cli's own pre-run check compiles
    /// it in and prints the same sentence without loading Atlas.dll: the CLI ships without it
    /// (see Atlas.Cli.csproj) and resolves it from the target directory, which is not yet
    /// possible when the environment is checked.</summary>
    public const string MissingInstallMessage =
        VariableName + " must point at a Vintage Story install containing " + LibraryFileName
        + " (current value: '{0}'); the embedded server cannot boot without it.";

    /// <summary>Checks that a directory looks like a Vintage Story install.</summary>
    /// <param name="directory">Value of the VINTAGE_STORY environment variable, possibly null.</param>
    /// <param name="fileExists">File-existence probe, injectable for tests.</param>
    /// <returns>The error message when the install is missing or incomplete; null when it is
    /// usable.</returns>
    public static string? Validate(string? directory, Func<string, bool> fileExists) =>
        string.IsNullOrEmpty(directory) || !fileExists(Path.Combine(directory, LibraryFileName))
            ? string.Format(CultureInfo.InvariantCulture, MissingInstallMessage, directory ?? "<unset>")
            : null;

    /// <summary>Locates the Vintage Story installation directory from the VINTAGE_STORY environment variable.</summary>
    /// <returns>The installation directory path.</returns>
    /// <exception cref="AtlasSetupException">Thrown when VINTAGE_STORY is not set or does not contain VintagestoryLib.dll.</exception>
    public static string Locate()
    {
        string? dir = Environment.GetEnvironmentVariable(VariableName);
        if (Validate(dir, File.Exists) is { } error)
        {
            throw new AtlasSetupException(error);
        }

        // Validate rejects null and empty, so past it the variable held a real path.
        return dir!;
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
