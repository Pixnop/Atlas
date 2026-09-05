namespace Atlas.Engine.Tests.Support;

/// <summary>One scenario result from a nested guinea pig run (<see cref="GuineaPigRunner"/>).</summary>
/// <param name="MethodName">The scenario method's name, without theory row arguments.</param>
/// <param name="DisplayName">The full display name, carrying the theory row arguments.</param>
/// <param name="Failure">The failure's type, message and stack trace, or <see langword="null"/>
/// when the scenario passed.</param>
internal sealed record ScenarioOutcome(string MethodName, string DisplayName, string? Failure)
{
    /// <summary>Gets a value indicating whether the scenario passed.</summary>
    public bool Passed => Failure is null;
}
