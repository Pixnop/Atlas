namespace Atlas.Internal.RealClient;

/// <summary>Everything the sandbox needs from the machine, resolved by an available
/// <see cref="ClientAvailability"/>: absolute tool paths, the install and the dotnet folder to
/// hand the client.</summary>
/// <param name="UnsharePath">The <c>unshare</c> program.</param>
/// <param name="SetprivPath">The <c>setpriv</c> program, which empties the capabilities of
/// everything that runs after the sandbox's mounts.</param>
/// <param name="XvfbPath">The <c>Xvfb</c> program.</param>
/// <param name="ImportPath">ImageMagick's <c>import</c>, for screenshots, or <see langword="null"/>
/// when it is not installed (a run then has no screenshots).</param>
/// <param name="InstallDirectory">The full client install, the folder holding the
/// <c>Vintagestory</c> executable.</param>
/// <param name="DotnetRoot">The dotnet folder handed to the client as <c>DOTNET_ROOT</c>.</param>
/// <param name="DataPath">The client data path.</param>
internal sealed record ClientToolchain(
    string UnsharePath,
    string SetprivPath,
    string XvfbPath,
    string? ImportPath,
    string InstallDirectory,
    string DotnetRoot,
    string DataPath)
{
    /// <summary>The client's own executable, in the install.</summary>
    public string ClientProgram => Path.Combine(InstallDirectory, ClientAvailability.ApphostName);

    /// <summary>The same toolchain with every path absolute, taken from this process's working
    /// folder and without a trailing separator. The inner script enters the install folder
    /// before it starts the client, so a relative data path would put the client's data inside
    /// the game's install, and a relative <c>DOTNET_ROOT</c> or tool would point somewhere else
    /// than where the availability check found it. Applying it twice changes nothing.</summary>
    /// <returns>The toolchain with absolute paths.</returns>
    public ClientToolchain Resolved() => this with
    {
        UnsharePath = Absolute(UnsharePath),
        SetprivPath = Absolute(SetprivPath),
        XvfbPath = Absolute(XvfbPath),
        ImportPath = ImportPath is null ? null : Absolute(ImportPath),
        InstallDirectory = Absolute(InstallDirectory),
        DotnetRoot = Absolute(DotnetRoot),
        DataPath = Absolute(DataPath),
    };

    private static string Absolute(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
