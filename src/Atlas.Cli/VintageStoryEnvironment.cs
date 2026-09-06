using System.Globalization;
using Atlas.Internal.Bootstrap;

namespace Atlas.Cli;

/// <summary>Checks VINTAGE_STORY before a run, so a missing install fails fast at the CLI
/// boundary with a clear message instead of deep inside the first scenario's host boot.</summary>
/// <remarks>The rule and its wording belong to <see cref="VsInstall"/>, and this reads them from
/// there: <see cref="VsInstall.VariableName"/>, <see cref="VsInstall.LibraryFileName"/> and
/// <see cref="VsInstall.MissingInstallMessage"/> are constants, so the compiler bakes them into
/// this assembly and the two checks cannot drift apart. What cannot be shared is the call:
/// <see cref="VsInstall.Validate"/> is a method, and calling it would make the CLI load Atlas.dll
/// here. The CLI ships without one (see Atlas.Cli.csproj) and resolves the target's own copy
/// through <see cref="ScenarioAssemblyResolver"/> or <see cref="StageAssemblyResolver"/>, neither
/// of which is installed yet when the environment is checked, so the four lines below stay on
/// this side of the boundary.</remarks>
internal static class VintageStoryEnvironment
{
    /// <summary>Checks that the given directory looks like a Vintage Story install.</summary>
    /// <param name="directory">Value of the VINTAGE_STORY environment variable, possibly null.</param>
    /// <param name="fileExists">File-existence probe, injectable for tests.</param>
    /// <returns>An error message when the install is missing or incomplete; null when valid.</returns>
    public static string? Validate(string? directory, Func<string, bool> fileExists) =>
        string.IsNullOrEmpty(directory) || !fileExists(Path.Combine(directory, VsInstall.LibraryFileName))
            ? string.Format(CultureInfo.InvariantCulture, VsInstall.MissingInstallMessage, directory ?? "<unset>")
            : null;

    /// <summary>Checks the install the current process's VINTAGE_STORY points at, the way every
    /// command that needs one does.</summary>
    /// <param name="installDir">Receives the variable's value: the validated install directory
    /// when this returns null, whatever the variable held (possibly null) otherwise.</param>
    /// <returns>An error message when the install is missing or incomplete; null when valid.</returns>
    public static string? ValidateCurrent(out string? installDir)
    {
        installDir = Environment.GetEnvironmentVariable(VsInstall.VariableName);
        return Validate(installDir, File.Exists);
    }
}
