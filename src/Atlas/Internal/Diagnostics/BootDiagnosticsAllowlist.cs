using System.Text.RegularExpressions;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Diagnostics;

/// <summary>Pure filter applying <c>[AtlasAllowBootDiagnostic(...)]</c> rules
/// (<see cref="AllowedBootDiagnostic"/>) to a boot diagnostics snapshot: only narrows what
/// <c>ServerHost.FinishBoot</c>'s strict check fails on, never what
/// <c>IWorldSession.BootDiagnostics</c> shows (that always reads the unfiltered recorder).</summary>
internal static partial class BootDiagnosticsAllowlist
{
    // Same bound as BootDiagnosticsLog's own patterns, and for the same reason: a rule's pattern
    // comes from a scenario author, not Atlas, so a pathological regex must fail safe (treated as
    // "did not match", see Matches below) instead of hanging the boot.
    private const int RegexTimeoutMs = 100;

    /// <summary>Removes every entry matched by at least one rule.</summary>
    /// <param name="entries">The entries to filter (typically a strict check's full snapshot).</param>
    /// <param name="rules">The allow rules in effect, assembly-level then class-level; an empty
    /// list is the common case and short-circuits without compiling anything.</param>
    /// <returns><paramref name="entries"/> unchanged when <paramref name="rules"/> is empty,
    /// otherwise a new list with every matched entry removed.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a rule's <see
    /// cref="AllowedBootDiagnostic.MessagePattern"/> is not a valid regular expression, or its
    /// <see cref="AllowedBootDiagnostic.Level"/> does not name (case-insensitively) one of
    /// <c>Warning</c>, <c>Error</c> or <c>Fatal</c>; a scenario author's typo belongs at boot, not
    /// silently allowing nothing.</exception>
    public static IReadOnlyList<BootDiagnosticEntry> Filter(
        IReadOnlyList<BootDiagnosticEntry> entries, IReadOnlyList<AllowedBootDiagnostic> rules)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
        {
            return entries;
        }

        List<CompiledRule> compiled = rules.Select(Compile).ToList();
        return entries.Where(entry => !compiled.Any(rule => Matches(rule, entry))).ToList();
    }

    private static CompiledRule Compile(AllowedBootDiagnostic rule)
    {
        Regex pattern;
        try
        {
            pattern = new Regex(rule.MessagePattern, RegexOptions.None, TimeSpan.FromMilliseconds(RegexTimeoutMs));
        }
        catch (ArgumentException ex)
        {
            throw new AtlasSetupException(
                $"[AtlasAllowBootDiagnostic] pattern '{rule.MessagePattern}' is not a valid regular " +
                $"expression: {ex.Message}");
        }

        EnumLogType? level = null;
        if (rule.Level is { } levelName)
        {
            // Case-insensitive on purpose ("warning" works, not just "Warning"): a scenario
            // author's Level string has nothing to gain from matching the engine enum's exact
            // casing, only a typo to lose to it. Enum.TryParse(ignoreCase: true) alone accepts far
            // more than a member name, though: a numeric string ("8"), a signed or zero-padded one
            // ("+8", "08"), and a comma-separated list ORed into a member the string never named
            // ("Error,Fatal" parses to Fatal). The explicit name check below closes all of those:
            // only a case-insensitive match against one of the enum's own member names reaches
            // Enum.TryParse at all, so a numeric or combined value never gets the chance to parse.
            if (!Enum.GetNames<EnumLogType>().Contains(levelName, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse(levelName, ignoreCase: true, out EnumLogType parsed)
                || parsed is not (EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
            {
                string declaredOn = rule.DeclaredOn.Length > 0 ? $" declared on {rule.DeclaredOn}" : string.Empty;
                throw new AtlasSetupException(
                    $"[AtlasAllowBootDiagnostic]{declaredOn}: Level '{levelName}' is not a recognized log " +
                    "level at Warning or above (expected one of: Warning, Error, Fatal).");
            }

            level = parsed;
        }

        return new CompiledRule(rule, pattern, level);
    }

    private static bool Matches(CompiledRule rule, BootDiagnosticEntry entry)
    {
        if (rule.Level is { } level && entry.Level != level)
        {
            return false;
        }

        if (rule.Source.Source is { } source && !string.Equals(entry.Source, source, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return rule.Pattern.IsMatch(entry.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail safe toward the entry still failing the boot: a rule that cannot be evaluated
            // must never silently suppress a real diagnostic.
            return false;
        }
    }

    private sealed record CompiledRule(AllowedBootDiagnostic Source, Regex Pattern, EnumLogType? Level);
}
