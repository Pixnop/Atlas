using System.Globalization;
using System.Text;
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

        var compiled = rules.Select(Compile).ToList();
        return entries.Where(entry => !compiled.Any(rule => Matches(rule, entry))).ToList();
    }

    /// <summary>Finds the rules that asked for entries the boot did not give them: a
    /// <see cref="AllowedBootDiagnostic.Required"/> rule that matched none, or a
    /// <see cref="AllowedBootDiagnostic.Count"/> rule that matched a different number. Each rule
    /// counts the entries it matches on its own, so one entry two rules match counts for both.</summary>
    /// <param name="entries">The boot's entries (a strict check's full snapshot).</param>
    /// <param name="rules">The allow rules in effect, assembly-level then class-level.</param>
    /// <returns>One line per unmet rule, in rule order, naming the rule, where it is declared and
    /// how many entries it matched; empty when every rule is met or none asks for anything.</returns>
    /// <exception cref="AtlasSetupException">Thrown for the same invalid rules as <see
    /// cref="Filter"/>, and when a <see cref="AllowedBootDiagnostic.Count"/> is below 1.</exception>
    public static IReadOnlyList<string> Unmet(
        IReadOnlyList<BootDiagnosticEntry> entries, IReadOnlyList<AllowedBootDiagnostic> rules)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rules);
        var unmet = new List<string>();
        foreach (AllowedBootDiagnostic rule in rules)
        {
            CompiledRule compiled = Compile(rule);
            if (!rule.Required && rule.Count is null)
            {
                continue;
            }

            int matched = entries.Count(entry => Matches(compiled, entry));
            if (rule.Count is { } expected)
            {
                if (matched != expected)
                {
                    unmet.Add($"{Describe(rule)} matched {Plural.Of(matched, "boot entry", "boot entries")}, expected exactly {expected}");
                }
            }
            else if (matched == 0)
            {
                unmet.Add($"{Describe(rule)} matched no boot entry, expected at least one");
            }
        }

        return unmet;
    }

    // The rule as its attribute is written, plus where it is declared, so the line points a
    // reader at the attribute to change.
    private static string Describe(AllowedBootDiagnostic rule)
    {
        var text = new StringBuilder($"[AtlasAllowBootDiagnostic(\"{rule.MessagePattern}\"");
        if (rule.Level is { } level)
        {
            text.Append($", Level = \"{level}\"");
        }

        if (rule.Source is { } source)
        {
            text.Append($", Source = \"{source}\"");
        }

        if (rule.Required)
        {
            text.Append(", Required = true");
        }

        if (rule.Count is { } count)
        {
            text.Append(CultureInfo.InvariantCulture, $", Count = {count}");
        }

        text.Append(")]");
        if (rule.DeclaredOn.Length > 0)
        {
            text.Append($" declared on {rule.DeclaredOn},");
        }

        return text.ToString();
    }

    private static CompiledRule Compile(AllowedBootDiagnostic rule)
    {
        if (rule.Count is < 1)
        {
            string declaredOn = rule.DeclaredOn.Length > 0 ? $" declared on {rule.DeclaredOn}" : string.Empty;
            throw new AtlasSetupException(
                $"[AtlasAllowBootDiagnostic]{declaredOn}: Count {rule.Count} is not valid, it must be at least 1 " +
                "(leave it out for no constraint on the number; a rule that must match nothing allows nothing).");
        }

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
