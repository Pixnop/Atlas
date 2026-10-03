using System.Text.Json;

namespace Atlas.Internal.RealClient;

/// <summary>What the client's apphost needs from the machine's .NET installation, and the pure
/// decision of which dotnet folder satisfies it. The apphost (the <c>Vintagestory</c> executable)
/// does not search <c>PATH</c>: it reads <c>DOTNET_ROOT</c>, then the folder registered in
/// <c>/etc/dotnet/install_location</c>, then the distribution's default folder. A private dotnet
/// that <c>PATH</c> finds but those do not is the case this exists to name before the client is
/// started, instead of letting the apphost die with a one-line message in a log nobody
/// opened.</summary>
/// <remarks>Only the default roll-forward policy is modelled, which is what the game ships: the
/// same major version, at or above the requested one. The arch-specific
/// <c>DOTNET_ROOT_X64</c> family is not read, so a machine that sets only that gets a skip with
/// a remedy that works (set <c>DOTNET_ROOT</c>), not a wrong start.</remarks>
internal static class DotnetRuntime
{
    /// <summary>The runtime configuration file the apphost reads next to itself.</summary>
    public const string RuntimeConfigFileName = "Vintagestory.runtimeconfig.json";

    /// <summary>The registered location file of the apphost's search.</summary>
    public const string RegisteredLocationFile = "/etc/dotnet/install_location";

    /// <summary>Folders the apphost falls back to after the environment and the registered
    /// location, in the order it tries them on the Linux distributions Atlas targets.</summary>
    public static readonly IReadOnlyList<string> DefaultRoots = ["/usr/share/dotnet", "/usr/lib/dotnet"];

    /// <summary>Reads the shared framework a runtime configuration asks for.</summary>
    /// <param name="runtimeConfigJson">The text of <c>*.runtimeconfig.json</c>, or
    /// <see langword="null"/> when the file is missing.</param>
    /// <returns>The requirement, or <see langword="null"/> when the text is missing, is not
    /// JSON, or names no framework (a self-contained app, which no client of the game
    /// is).</returns>
    public static RuntimeRequirement? ReadRequirement(string? runtimeConfigJson)
    {
        if (string.IsNullOrWhiteSpace(runtimeConfigJson))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(runtimeConfigJson);
            if (doc.RootElement.TryGetProperty("runtimeOptions", out JsonElement options)
                && options.TryGetProperty("framework", out JsonElement framework)
                && framework.TryGetProperty("name", out JsonElement name)
                && framework.TryGetProperty("version", out JsonElement version)
                && Version.TryParse(version.GetString(), out Version? parsed)
                && name.GetString() is { Length: > 0 } frameworkName)
            {
                return new RuntimeRequirement(frameworkName, parsed);
            }
        }
        catch (JsonException)
        {
            // Not JSON: the same answer as no requirement, the caller names the file.
        }

        return null;
    }

    /// <summary>The dotnet folders the apphost would look in, in its order, without
    /// duplicates.</summary>
    /// <param name="dotnetRootVariable">The value of <c>DOTNET_ROOT</c>, if set.</param>
    /// <param name="registeredLocation">The text of <see cref="RegisteredLocationFile"/>, or
    /// <see langword="null"/> when there is none.</param>
    /// <returns>The candidate folders.</returns>
    public static IReadOnlyList<string> CandidateRoots(string? dotnetRootVariable, string? registeredLocation)
    {
        string? registered = registeredLocation?.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault();
        return
        [
            .. new[] { dotnetRootVariable, registered }
                .Concat(DefaultRoots)
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(root => root!.Trim())
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>Picks the first candidate that holds a runtime satisfying the requirement.</summary>
    /// <param name="requirement">What the client asks for.</param>
    /// <param name="candidates">The folders to look in, in order (see
    /// <see cref="CandidateRoots"/>).</param>
    /// <param name="listRuntimeVersions">Lists the version folder names under
    /// <c>&lt;root&gt;/shared/&lt;framework&gt;</c> for a root (empty when there are
    /// none).</param>
    /// <returns>The dotnet folder to hand the client as <c>DOTNET_ROOT</c>, or
    /// <see langword="null"/> when none qualifies.</returns>
    public static string? FindRoot(
        RuntimeRequirement requirement,
        IEnumerable<string> candidates,
        Func<string, IEnumerable<string>> listRuntimeVersions)
        => candidates.FirstOrDefault(root => listRuntimeVersions(root).Any(requirement.IsSatisfiedBy));
}
