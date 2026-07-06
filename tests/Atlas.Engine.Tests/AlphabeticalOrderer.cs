using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.Engine.Tests;

/// <summary>Orders test cases by method name. xUnit guarantees no execution order within a
/// class by default, and <see cref="AdapterRollbackTests"/> needs its polluting scenario to run
/// strictly before the scenario that asserts the pollution was rolled back.</summary>
public class AlphabeticalOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(tc => tc.TestMethod.Method.Name, StringComparer.Ordinal);
}
