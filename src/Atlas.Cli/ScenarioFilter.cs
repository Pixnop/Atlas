namespace Atlas.Cli;

/// <summary>Matches scenario display names against the optional `--filter` substring
/// (ordinal, case-insensitive). A null or empty pattern matches everything.</summary>
internal sealed class ScenarioFilter
{
    private readonly string? _substring;

    /// <summary>Initializes a new instance of the <see cref="ScenarioFilter"/> class.</summary>
    /// <param name="substring">The substring to look for; null or empty matches everything.</param>
    public ScenarioFilter(string? substring) => _substring = substring;

    /// <summary>Gets a value indicating whether this filter can exclude anything.</summary>
    public bool IsSelective => !string.IsNullOrEmpty(_substring);

    /// <summary>Decides whether a scenario is selected by this filter.</summary>
    /// <param name="displayName">The scenario's display name (typically Namespace.Class.Method).</param>
    /// <returns>True when the scenario should run or be listed.</returns>
    public bool Matches(string displayName) =>
        !IsSelective || displayName.Contains(_substring!, StringComparison.OrdinalIgnoreCase);
}
