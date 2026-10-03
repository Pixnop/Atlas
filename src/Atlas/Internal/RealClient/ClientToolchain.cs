namespace Atlas.Internal.RealClient;

/// <summary>Everything the sandbox needs from the machine, resolved by an available
/// <see cref="ClientAvailability"/>: absolute tool paths, the install and the dotnet folder to
/// hand the client.</summary>
/// <param name="UnsharePath">The <c>unshare</c> program.</param>
/// <param name="XvfbPath">The <c>Xvfb</c> program.</param>
/// <param name="ImportPath">ImageMagick's <c>import</c>, for screenshots, or <see langword="null"/>
/// when it is not installed (a run then has no screenshots).</param>
/// <param name="InstallDirectory">The full client install, the folder holding the
/// <c>Vintagestory</c> executable.</param>
/// <param name="DotnetRoot">The dotnet folder handed to the client as <c>DOTNET_ROOT</c>.</param>
/// <param name="DataPath">The client data path, as given.</param>
internal sealed record ClientToolchain(
    string UnsharePath,
    string XvfbPath,
    string? ImportPath,
    string InstallDirectory,
    string DotnetRoot,
    string DataPath)
{
    /// <summary>The client's own executable, in the install.</summary>
    public string ClientProgram => Path.Combine(InstallDirectory, ClientAvailability.ApphostName);
}
