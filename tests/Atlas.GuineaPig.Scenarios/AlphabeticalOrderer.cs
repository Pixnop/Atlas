using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Orders test cases by method name. xUnit guarantees no execution order within a
/// class by default, and <see cref="DeadHostSequenceScenarios"/> needs its crash scenario to
/// run strictly before its fail-fast scenario for the sequence to mean anything.</summary>
public class AlphabeticalOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(tc => tc.TestMethod.Method.Name, StringComparer.Ordinal);
}
