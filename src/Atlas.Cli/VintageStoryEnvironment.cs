namespace Atlas.Cli;

/// <summary>Validates the VINTAGE_STORY environment variable before a run, mirroring the check the
/// engine's own VsInstall.Locate performs at boot, so a missing install fails fast at the CLI
/// boundary with a clear message instead of deep inside the first scenario's fixture.</summary>
internal static class VintageStoryEnvironment
{
    /// <summary>Name of the environment variable pointing at the Vintage Story install.</summary>
    public const string VariableName = "VINTAGE_STORY";

    /// <summary>Checks that the given directory looks like a Vintage Story install.</summary>
    /// <param name="directory">Value of the VINTAGE_STORY environment variable, possibly null.</param>
    /// <param name="fileExists">File-existence probe, injectable for tests.</param>
    /// <returns>An error message when the install is missing or incomplete; null when valid.</returns>
    public static string? Validate(string? directory, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(directory) || !fileExists(Path.Combine(directory, "VintagestoryLib.dll")))
        {
            return "VINTAGE_STORY must point at a Vintage Story install containing VintagestoryLib.dll " +
                $"(current value: '{directory ?? "<unset>"}'); the embedded server cannot boot without it.";
        }

        return null;
    }

    /// <summary>Checks the install the current process's VINTAGE_STORY points at, the way every
    /// command that needs one does.</summary>
    /// <param name="installDir">Receives the variable's value: the validated install directory
    /// when this returns null, whatever the variable held (possibly null) otherwise.</param>
    /// <returns>An error message when the install is missing or incomplete; null when valid.</returns>
    public static string? ValidateCurrent(out string? installDir)
    {
        installDir = Environment.GetEnvironmentVariable(VariableName);
        return Validate(installDir, File.Exists);
    }
}
