namespace Atlas.Internal.RealClient;

/// <summary>A shared framework and the lowest version of it a client asks for.</summary>
/// <param name="Framework">The framework name, for example <c>Microsoft.NETCore.App</c>.</param>
/// <param name="Minimum">The requested version, the lowest that qualifies.</param>
internal sealed record RuntimeRequirement(string Framework, Version Minimum)
{
    /// <summary>Whether an installed runtime satisfies the requirement under the default
    /// roll-forward policy: the same major version at or above the requested one. A
    /// pre-release runtime (a folder name with a suffix) never qualifies.</summary>
    /// <param name="installedVersion">The name of a version folder under the framework's
    /// shared folder.</param>
    /// <returns>Whether the runtime qualifies.</returns>
    public bool IsSatisfiedBy(string installedVersion)
        => Version.TryParse(installedVersion, out Version? installed)
           && installed.Major == Minimum.Major
           && installed >= Minimum;
}
