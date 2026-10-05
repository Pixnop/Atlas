using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>The pure half of the boot's game version report: words the one stderr line that
/// names the game the server runs on and the game the scenarios were compiled against, and
/// decides whether an assembly that asked for the two to match gets to boot. Kept free of the
/// engine so both messages are testable without an install; the host passes the running version
/// (<c>EngineCompat.ShortGameVersion</c>, read from the loaded engine's metadata) in.</summary>
/// <remarks>The line names a version, not a build: a fork rebuilt at the same version reports the
/// same <c>GameVersion.ShortGameVersion</c> as vanilla and cannot be told from it this way, which
/// is also why the staging preflight compares file content (<see cref="ApiCopySync"/>) and never
/// versions.</remarks>
internal static class GameVersionBoot
{
    // Every line written so far in this process. The boot runs once per class and once more per
    // recycle, restart or retry, and the line says the same thing each time.
    private static readonly HashSet<string> Announced = [];

    /// <summary>Words the boot line.</summary>
    /// <param name="runningVersion">The game version of the install the server runs on.</param>
    /// <param name="install">The install directory.</param>
    /// <param name="compiledVersion">The version the scenarios were compiled against, or
    /// <see langword="null"/> when the assembly carries none.</param>
    /// <returns>The line, without a trailing newline.</returns>
    public static string Describe(string runningVersion, string install, string? compiledVersion)
        => $"[Atlas] game {runningVersion} from '{install}' (" + (compiledVersion is null
            ? "scenarios carry no compiled game version: the build stamped none, see the Atlas build targets)"
            : $"scenarios compiled against {compiledVersion})");

    /// <summary>Writes the boot line, once per distinct line in this process, then refuses the
    /// boot when the assembly required the compiled version and the install is not it.</summary>
    /// <param name="runningVersion">The game version of the install the server runs on.</param>
    /// <param name="install">The install directory.</param>
    /// <param name="compiled">What the scenario assembly says, or <see langword="null"/> for a
    /// host no scenario class owns.</param>
    /// <param name="write">Where the line goes (stderr, next to the "[Atlas] staged mod" lines).</param>
    /// <exception cref="AtlasSetupException">Thrown when the assembly required the version it was
    /// compiled against and either the running version differs or the assembly carries no
    /// version to compare. The line is written first, so a failing boot still says which game it
    /// ran on.</exception>
    public static void Check(string runningVersion, string install, CompiledGameVersion? compiled, Action<string> write)
    {
        string line = Describe(runningVersion, install, compiled?.Version);
        bool first;
        lock (Announced)
        {
            first = Announced.Add(line);
        }

        if (first)
        {
            write(line);
        }

        if (compiled is { Required: true } && !string.Equals(compiled.Version, runningVersion, StringComparison.Ordinal))
        {
            throw new AtlasSetupException(Refusal(runningVersion, install, compiled.Version));
        }
    }

    private static string Refusal(string runningVersion, string install, string? compiledVersion)
    {
        const string attribute = "[assembly: AtlasRequireCompiledGameVersion]";
        return compiledVersion is null
            ? $"The scenario assembly asks for the game version it was compiled against ({attribute}), but it carries " +
              $"no compiled game version to compare with the install at '{install}' (game {runningVersion}): the build " +
              "stamped none. The Atlas build targets (build/Atlas.E2E.targets, shipped in the Pixnop.Atlas.XUnit " +
              "package) stamp it in a C# project that references VintagestoryAPI itself, with no Aliases " +
              "metadata other than global on the reference. Build it that way, or remove the attribute."
            : $"The scenario assembly was compiled against game {compiledVersion} and asks for exactly that " +
              $"version ({attribute}), but VINTAGE_STORY points at '{install}', which is game {runningVersion}. " +
              $"Point VINTAGE_STORY at a {compiledVersion} install, rebuild the scenarios against this install, " +
              "or remove the attribute to run a build on another install (the cross-install run, issue #49).";
    }
}
