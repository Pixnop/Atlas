namespace Atlas.Cli;

/// <summary>Enforces the single-match rule of `atlas fixture --scenario`: the substring must
/// select exactly one scenario, because the fixture is the product of one deliberate builder,
/// not of whatever set a loose substring happens to catch. Pure, so the rule and its error
/// texts are unit-testable without discovery.</summary>
internal static class FixtureScenarioSelection
{
    /// <summary>Validates that the discovery result contains exactly one scenario.</summary>
    /// <param name="matches">The scenarios the substring matched.</param>
    /// <param name="substring">The user's --scenario substring, for the error text.</param>
    /// <returns>A usage error (zero matches, or several with the candidates listed), or null
    /// when exactly one scenario matched.</returns>
    public static string? Validate(IReadOnlyList<DiscoveredScenario> matches, string substring)
    {
        if (matches.Count == 1)
        {
            return null;
        }

        if (matches.Count == 0)
        {
            return $"no scenario matches --scenario '{substring}' (try 'atlas run <dll> --list' to see the names)";
        }

        IEnumerable<string> candidates = matches.Select(scenario => "  " + scenario.DisplayName);
        return $"--scenario '{substring}' matches {matches.Count} scenarios; narrow it down to exactly one:"
            + Environment.NewLine + string.Join(Environment.NewLine, candidates);
    }
}
