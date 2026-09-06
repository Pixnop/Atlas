namespace Atlas.Engine.Tests;

/// <summary>Runs the guinea pig <c>TheoryRowScenarios</c> class through the nested runner
/// (<see cref="GuineaPigRunner"/>) and asserts the documented per-row shapes of
/// <c>[AtlasTheory]</c>: one result per <c>[InlineData]</c> row with the row's arguments in the
/// display name, a failing row leaving its sibling rows passing, non-serializable
/// <c>[MemberData]</c> rows still executing through the runtime-enumeration fallback, and a
/// data-less theory surfacing xUnit's own "No data found for ..." failure. Nested because a test
/// cannot assert its own row's failure, same technique as <c>NestedRunnerTests</c>.</summary>
[Trait("Category", "E2E")]
public class TheoryNestedRunnerTests
{
    [Fact]
    public async Task TheoryRows_Should_PassAndFailIndependently_When_RunNested()
    {
        // Pre-enumeration on, as under `dotnet test`: serializable [InlineData] rows become one
        // AtlasTestCase per row at discovery time, while the non-serializable [MemberData] rows
        // fall back to the single runtime-enumerating AtlasTheoryTestCase. One server boot plus
        // a handful of single-tick scenarios.
        IReadOnlyList<ScenarioOutcome> outcomes = await GuineaPigRunner.RunAsync(
            ["TheoryRowScenarios"], TimeSpan.FromMinutes(3), preEnumerateTheories: true);

        List<string> passedNames = [.. outcomes.Where(outcome => outcome.Passed).Select(outcome => outcome.DisplayName)];

        // Grouped, not keyed directly: a duplicate display name must not throw before the dump
        // below can be built, which is the only diagnostic this test has.
        Dictionary<string, string> failures = outcomes
            .Where(outcome => !outcome.Passed)
            .GroupBy(outcome => outcome.DisplayName)
            .ToDictionary(group => group.Key, group => group.Last().Failure!);
        string dump = "passed: [" + string.Join(", ", passedNames) + "]\nfailed:\n"
            + string.Join("\n----\n", failures.Select(f => f.Key + " => " + f.Value));

        // [InlineData]: one result per row, arguments in display names, only row 2 fails.
        Assert.True(passedNames.Count == 4, "Expected 4 passes, got:\n" + dump);
        Assert.True(failures.Count == 2, "Expected 2 failures, got:\n" + dump);
        Assert.Contains(passedNames, n => n.Contains("Theory_Should_FailOnlySecondRow_When_RowsRunIndependently(row: 1)"));
        Assert.Contains(passedNames, n => n.Contains("Theory_Should_FailOnlySecondRow_When_RowsRunIndependently(row: 3)"));
        (string row2Name, string row2Failure) = Assert.Single(failures, f => f.Key.Contains("Theory_Should_FailOnlySecondRow_When_RowsRunIndependently"));
        Assert.Contains("(row: 2)", row2Name);
        Assert.Contains("NotEqual", row2Failure);

        // [MemberData] with non-serializable rows: the fallback still executed each row, with
        // the row's payload (ToString) in its display name.
        Assert.Contains(passedNames, n => n.Contains("Theory_Should_RunEachRow_When_DataRowsAreNotSerializable") && n.Contains("alpha"));
        Assert.Contains(passedNames, n => n.Contains("Theory_Should_RunEachRow_When_DataRowsAreNotSerializable") && n.Contains("beta"));

        // No data attributes: xUnit's own execution-error case, not a silent pass.
        (_, string noData) = Assert.Single(failures, f => f.Key.Contains("Theory_Should_FailWithNoDataFound_When_TheoryHasNoDataAttributes"));
        Assert.Contains("No data found", noData);
    }
}
