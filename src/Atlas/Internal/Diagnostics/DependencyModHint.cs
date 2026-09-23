using Atlas.Api;

namespace Atlas.Internal.Diagnostics;

/// <summary>Pure detector for one specific, reliably-recognizable boot diagnostic shape: a
/// dependency dll listed in <c>[AtlasMods]</c> as if it were its own mod. <c>ModStager.Stage</c>
/// copies every <c>[AtlasMods]</c> path straight into the mod folder, so a plain dll with no
/// <c>ModSystem</c> and no <c>ModInfoAttribute</c> lands there as its own top-level mod, and the
/// engine rejects it with a fixed message (<see cref="EngineMessage"/>, decompile- and
/// headless-verified against 1.22.7 in <c>BootDiagnosticsTests.
/// StartAsync_Should_HintADependencyDll_When_APlainLibraryWithNoModSystemIsStagedThroughAtlasMods</c>)
/// through <c>ModContainer.LoadModInfo</c>'s catch, unverified (no per-mod-logger channel exists
/// yet at that point in boot, see <see cref="BootDiagnosticEntry.Source"/>). A genuine mod dll
/// that really is missing a <c>ModSystem</c> or <c>ModInfoAttribute</c> reads identically to the
/// engine, so the hint below is phrased as a guess, not a verdict, and only offered at all when
/// the failing file is one Atlas itself staged.</summary>
internal static class DependencyModHint
{
    private const string WikiUrl = "https://github.com/Pixnop/Atlas/wiki/Mod-Staging";

    // Vintagestory.Common.ModContainer.LoadModInfoFromCode / LoadAssembly's literal message.
    // Matched as fixed text, not through EngineCompat: there is no reflected shape to probe here,
    // only a log message to recognize, the same reasoning BootDiagnosticsLog.EnvironmentalNoise
    // already applies to a different engine log line.
    private const string EngineMessage =
        "declared as code mod, but there are no .dll files that contain at least one ModSystem " +
        "or has a ModInfo attribute";

    /// <summary>What to append to <paramref name="entry"/>'s rendered line, or
    /// <see langword="null"/> when it is not this shape.</summary>
    /// <param name="entry">One boot diagnostic entry.</param>
    /// <param name="modPaths">The paths this host's <c>[AtlasMods]</c> staged, exactly as
    /// declared (relative or absolute); only their file names are compared; a staged dll's
    /// <see cref="BootDiagnosticEntry.SourceHint"/> is always the bare staged file name
    /// (<c>ModStager.Stage</c> flattens every path to just its <c>Path.GetFileName</c>).</param>
    /// <returns>A short hint naming the likely fix, when <paramref name="entry"/> is an
    /// unknown-source entry whose message is the engine's own "declared as code mod" failure and
    /// whose hinted file is one of <paramref name="modPaths"/>; <see langword="null"/>
    /// otherwise (a different entry, or a failing dll Atlas did not itself stage, where guessing
    /// would be a shot in the dark).</returns>
    public static string? Describe(BootDiagnosticEntry entry, IReadOnlyList<string> modPaths)
    {
        if (entry.Source != BootDiagnosticsLog.UnknownSource
            || entry.SourceHint is not { } hint
            || !entry.Message.Contains(EngineMessage, StringComparison.Ordinal)
            || !modPaths.Any(path => string.Equals(Path.GetFileName(path), hint, StringComparison.Ordinal)))
        {
            return null;
        }

        return "This looks like a dependency dll staged as a mod of its own: stage it next to the mod " +
            $"instead of listing it in AtlasMods. See {WikiUrl}.";
    }
}
