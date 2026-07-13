using System.Collections.Concurrent;
using Atlas.Cli;
using Xunit.Runners;

namespace Atlas.Engine.Tests;

/// <summary>Runs the guinea pig <c>TheoryRowScenarios</c> class through an in-process xunit
/// runner and asserts the documented per-row shapes of <c>[AtlasTheory]</c>: one result per
/// <c>[InlineData]</c> row with the row's arguments in the display name, a failing row leaving
/// its sibling rows passing, non-serializable <c>[MemberData]</c> rows still executing through
/// the runtime-enumeration fallback, and a data-less theory surfacing xUnit's own
/// "No data found for ..." failure. Nested because a test cannot assert its own row's failure,
/// same technique as <c>NestedRunnerTests</c>.</summary>
[Trait("Category", "E2E")]
public class TheoryNestedRunnerTests
{
    [Fact]
    public async Task TheoryRows_Should_PassAndFailIndependently_When_RunNested()
    {
        string dll = Path.Combine(
            Path.GetDirectoryName(typeof(TheoryNestedRunnerTests).Assembly.Location)!,
            "Atlas.GuineaPig.Scenarios.dll");
        Assert.True(File.Exists(dll), $"Guinea pig assembly not found at '{dll}'.");

        var failures = new ConcurrentDictionary<string, string>();
        var passedNames = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = AssemblyRunner.WithoutAppDomain(dll);
        try
        {
            runner.OnTestFailed = info => failures[info.TestDisplayName] = $"{info.ExceptionType}: {info.ExceptionMessage}\n{info.ExceptionStackTrace}";
            runner.OnTestPassed = info => passedNames.Enqueue(info.TestDisplayName);
            runner.OnExecutionComplete = _ => done.TrySetResult();
            runner.Start(new AssemblyRunnerStartOptions
            {
                // Pre-enumeration on, as under `dotnet test`: serializable [InlineData] rows become
                // one AtlasTestCase per row at discovery time, while the non-serializable
                // [MemberData] rows fall back to the single runtime-enumerating AtlasTheoryTestCase.
                PreEnumerateTheories = true,
                TypesToRun = new[] { "Atlas.GuineaPig.Scenarios.TheoryRowScenarios" }
            });

            // One server boot plus a handful of single-tick scenarios.
            await done.Task.WaitAsync(TimeSpan.FromMinutes(3));

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

            // [MemberData] with non-serializable rows: the fallback still executed each row,
            // with the row's payload (ToString) in its display name.
            Assert.Contains(passedNames, n => n.Contains("Theory_Should_RunEachRow_When_DataRowsAreNotSerializable") && n.Contains("alpha"));
            Assert.Contains(passedNames, n => n.Contains("Theory_Should_RunEachRow_When_DataRowsAreNotSerializable") && n.Contains("beta"));

            // No data attributes: xUnit's own execution-error case, not a silent pass.
            (_, string noData) = Assert.Single(failures, f => f.Key.Contains("Theory_Should_FailWithNoDataFound_When_TheoryHasNoDataAttributes"));
            Assert.Contains("No data found", noData);
        }
        finally
        {
            // Not a `using`: see RunnerDisposal (issue #59); wait for Idle (bounded) and prefer
            // leaking the runner over disposing it hot.
            RunnerDisposal.DisposeWhenIdle(runner);
        }
    }
}
