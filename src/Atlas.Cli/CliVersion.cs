using System.Reflection;

namespace Atlas.Cli;

/// <summary>Resolves the version string printed by `atlas --version`: the assembly's
/// informational version (aligned with the NuGet package version since 0.6.0) with the SemVer
/// `+sha` build metadata stripped, so the output matches the published package version exactly
/// and stays script-friendly. The commit sha remains available in the assembly metadata for
/// forensics; it just is not part of the version a user compares against a release.</summary>
internal static class CliVersion
{
    /// <summary>Fallback printed when the assembly carries no informational version.</summary>
    internal const string Unknown = "unknown";

    /// <summary>Resolves the version of the running CLI assembly.</summary>
    /// <returns>The package-style version string.</returns>
    public static string Resolve() => FromInformationalVersion(
        typeof(CliVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Normalizes a raw informational version into the printed version string.</summary>
    /// <param name="informationalVersion">The raw AssemblyInformationalVersion value, or null when
    /// the assembly carries none.</param>
    /// <returns>The version without SemVer build metadata, or <see cref="Unknown"/>.</returns>
    public static string FromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return Unknown;
        }

        int metadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        string version = metadata < 0 ? informationalVersion : informationalVersion[..metadata];
        return version.Length == 0 ? Unknown : version;
    }
}
