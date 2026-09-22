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

    /// <summary>Asserts that a captured <see cref="Failure"/> text contains
    /// <paramref name="expectedSubstring"/>, printing the full failure text in the assertion's
    /// own message instead of leaving it to xUnit's default <c>Assert.Contains(string, string)</c>
    /// message. That default truncates the displayed value at 41 characters
    /// (<c>Xunit.Internal.AssertHelper.ShortenAndEncodeString</c>), which is exactly what left
    /// issue #118's one flaky occurrence undiagnosable: both the console output and the TRX
    /// showed the same 41-character cut of the exception text and nothing past it.</summary>
    /// <param name="expectedSubstring">The substring a documented failure shape must contain.</param>
    /// <param name="failureText">A <see cref="Failure"/> value to check.</param>
    public static void AssertFailureContains(string expectedSubstring, string failureText)
    {
        string message = $"Expected the failure text to contain \"{expectedSubstring}\"." +
            $"{Environment.NewLine}Full failure text:{Environment.NewLine}{failureText}";
        Assert.True(failureText.Contains(expectedSubstring, StringComparison.Ordinal), message);
    }
}
